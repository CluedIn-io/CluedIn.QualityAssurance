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
        return CommitAllDataSetAsync(cancellationToken);
    }

    protected override async Task<IEnumerable<SetupOperation>> GetSetupOperationsAsync(bool isReingestion, CancellationToken cancellationToken)
    {
        var operations = new List<SetupOperation>();

        if (isReingestion)
        {
            operations.Add(new SetupOperation(LoginAsync));
            operations.Add(new SetupOperation(SubmitSampleClueAsync)); // not really necessary.
        }
        else
        {
            operations.Add(new SetupOperation(CreateOrganizationAsync));
            operations.Add(new SetupOperation(LoginAsync));
            operations.Add(new SetupOperation(SubmitSampleClueAsync));
            foreach (var fileSource in FileSources)
            {
                operations.Add(CreateSetupOperation(fileSource, CreateDataSourceSetAsync));
                operations.Add(CreateSetupOperation(fileSource, UploadFileAsync));
                operations.Add(CreateSetupOperation(fileSource, GetDataSetIdAsync));
                operations.Add(CreateSetupOperation(fileSource, AutoAnnotateAsync));
                operations.Add(CreateSetupOperation(fileSource, GetAnnotationIdAsync));
                await AddMappingModificationsAsync(fileSource, operations, cancellationToken).ConfigureAwait(false);
            }
        }
        return operations;
    }

    private async Task UploadFileAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);

        var requestUri = new Uri(serverUris.UploadApiUri, $"api/datasourceset/{fileSource.DataSourceSetId}/file");
        using (var multipartFormContent = new MultipartFormDataContent())
        {
            //Load the file and set the file's Content-Type header
            var stream = GetUploadFileStream(fileSource);
            var fileStreamContent = new StreamContent(stream);
            fileStreamContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(fileSource.UploadFilePath));

            //Add the file
            multipartFormContent.Add(fileStreamContent, name: fileSource.UploadFilePath, fileName: fileSource.UploadFilePath);

            //Send it
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = multipartFormContent,
            };
            requestMessage.Headers.Add("name", fileSource.UploadFilePath);
            requestMessage.Headers.Add("clientId", Organization.ClientId);
            var response = await SendRequestAsync(requestMessage, cancellationToken, true, client => client.Timeout = Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            var result = await response.Content.DeserializeToAnonymousTypeAsync(new
            {
                id = (int?)null,
            });
            int? dataSourceId = result?.id ?? throw new InvalidOperationException("DataSourceId is not found in result.");

            fileSource.DataSourceId = dataSourceId.Value;
        }
    }

    private static string GetMimeType(string filePath)
    {
        // TODO: File type detection using magic bytes and then finally fallback to extension
        var extension = Path.GetExtension(filePath);

        return extension switch
        {
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => throw new NotSupportedException($"File extension '{extension}' is not supported.")
        };
    }

    private async Task GetDataSetIdAsync(FileSource fileSource, CancellationToken cancellationToken)
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
                var response = await GetDataSourceByIdAsync(fileSource, cancellationToken).ConfigureAwait(false);
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

                Guid? dataSetId = result.data?.inbound?.dataSource?.dataSets?.SingleOrDefault(x => x?.dataSource?.id == fileSource.DataSourceId)?.id ?? throw new InvalidOperationException("DataSetId is not found in result.");

                fileSource.DataSetId = dataSetId.Value;
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

    private async Task CommitAllDataSetAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private async Task CommitDataSetAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;
        var client = HttpClientFactory.CreateClient(Constants.AllowUntrustedSSLClient);

        var body = await GetRequestTemplateAsync(nameof(CommitDataSetAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{DataSetId}}", fileSource.DataSetId.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, "application/json"),
        };

        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
    }
}
