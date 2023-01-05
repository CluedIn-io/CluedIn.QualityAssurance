using System.Net.Http.Headers;
using System.Text;
using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Services.PostOperationActions;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Services.ResultWriters;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.FileUpload;

internal class FileUploadOperation : FileSourceOperationBase<FileUploadOptions>
{
    private const int TotalGetDataSetIdRetries = 3;
    private static readonly TimeSpan DelayBetweenGetDataSetIdRetries = TimeSpan.FromSeconds(30);

    public FileUploadOperation(
        ILogger<FileUploadOperation> logger,
        IEnvironment testEnvironment,
        IEnumerable<IResultWriter> resultWriters,
        IRabbitMQCompletionChecker rabbitMqCompletionChecker,
        IEnumerable<IPostOperationAction> postOperationActions,
        IHttpClientFactory httpClientFactory)
        : base(logger, testEnvironment, resultWriters, rabbitMqCompletionChecker, postOperationActions, httpClientFactory)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    private ILogger<FileUploadOperation> Logger { get; }

    protected override Task ExecuteIngestionAsync(CancellationToken cancellationToken)
    {
        return CommitDataSetAsync(cancellationToken);
    }

    protected override IEnumerable<Func<CancellationToken, Task>> GetSetupOperations(bool isReingestion)
    {
        var operations = new List<Func<CancellationToken, Task>>();

        if (isReingestion)
        {
            operations.Add(LoginAsync);
            operations.Add(SubmitSampleClueAsync); // not really necessary.
        }
        else
        {
            operations.Add(CreateOrganizationAsync);
            operations.Add(LoginAsync);
            operations.Add(SubmitSampleClueAsync);
            operations.Add(CreateDataSourceSetAsync);
            operations.Add(UploadFileAsync);
            operations.Add(GetDataSetIdAsync);
            operations.Add(AutoAnnotateAsync);
            operations.Add(GetAnnotationIdAsync);
            AddMappingModifications(operations);
        }
        return operations;
    }

    private async Task UploadFileAsync(CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);

        var requestUri = new Uri(serverUris.UploadApiUri, $"api/datasourceset/{FileSource.DataSourceSetId}/file");
        using (var multipartFormContent = new MultipartFormDataContent())
        {
            //Load the file and set the file's Content-Type header
            var stream = GetUploadFileStream();
            var fileStreamContent = new StreamContent(stream);
            fileStreamContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

            //Add the file
            multipartFormContent.Add(fileStreamContent, name: FileSource.UploadFilePath, fileName: FileSource.UploadFilePath);

            //Send it
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = multipartFormContent,
            };
            requestMessage.Headers.Add("name", FileSource.UploadFilePath);
            requestMessage.Headers.Add("clientId", Organization.ClientId);
            var response = await SendRequestAsync(requestMessage, cancellationToken, true, client => client.Timeout = Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            var result = await response.Content.DeserializeToAnonymousTypeAsync(new
            {
                id = (int?)null,
            });
            int? dataSourceId = result?.id ?? throw new InvalidOperationException("DataSourceId is not found in result.");

            FileSource.DataSourceId = dataSourceId.Value;
        }
    }

    private async Task GetDataSetIdAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < TotalGetDataSetIdRetries; ++i)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogWarning("Cancelling operation of GetDataSetIdAsync because cancellation is requested.");
                return;
            }
            try
            {
                var response = await GetDataSourceByIdAsync(cancellationToken).ConfigureAwait(false);
                var result = await response.Content
                    .DeserializeToAnonymousTypeAsync(new
                    {
                        data = new
                        {
                            inbound = new
                            {
                                dataSource = new
                                {
                                    dataSets = new[]
                                    {
                            new
                            {
                                id = (Guid?)null,
                                dataSource = new
                                {
                                    id = (int?)null,
                                }
                            },
                                    },
                                },
                            },
                        },
                    }).ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

                Guid? dataSetId = result.data?.inbound?.dataSource?.dataSets?.SingleOrDefault(x => x?.dataSource?.id == FileSource.DataSourceId)?.id ?? throw new InvalidOperationException("DataSetId is not found in result.");

                FileSource.DataSetId = dataSetId.Value;
                return;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to get data set id. Will retry in {TimeBetweenRetries}", DelayBetweenGetDataSetIdRetries);
                await Task.Delay(DelayBetweenGetDataSetIdRetries, cancellationToken);
            }

        }
        throw new InvalidOperationException("Failed to get dataset id after multiple tries");
    }

    private async Task CommitDataSetAsync(CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;
        var client = HttpClientFactory.CreateClient(Constants.AllowUntrustedSSLClient);

        var body = await GetRequestTemplateAsync(nameof(CommitDataSetAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{DataSetId}}", FileSource.DataSetId.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, "application/json"),
        };

        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
    }
}
