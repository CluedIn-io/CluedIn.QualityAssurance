using System;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Services.PostOperationActions;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Services.ResultWriters;
using Microsoft.Extensions.Logging;
using SystemEnvironment = System.Environment;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.FileUpload;

internal class FileUploadOperation : FileSourceOperation<FileUploadOptions>
{
    private const int TotalGetDataSetIdRetries = 10;
    private static readonly TimeSpan DelayBetweenGetDataSetIdRetries = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DelayBetweenDataSetCommits = TimeSpan.FromSeconds(5);

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
                if (Options.UseLegacyFileUpload)
                {
                    this.Logger.LogInformation("Using legacy file upload.");
                    operations.Add(CreateSetupOperation(fileSource, LegacyUploadFileAsync));
                }
                else
                {
                    operations.Add(CreateSetupOperation(fileSource, ResumeUploadRequestAsync));
                }

                operations.Add(CreateSetupOperation(fileSource, GetDataSetIdAsync));
                await AddMappingOperationsAsync(operations, fileSource, cancellationToken).ConfigureAwait(false);
            }
        }
        return operations;
    }

    private async Task ResumeUploadRequestAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = new Uri(serverUris.UploadApiUri, $"resume-upload-request");
        var client = HttpClientFactory.CreateClient(Constants.AllowUntrustedSSLClient);

        using var stream = GetUploadFileStream(fileSource);
        var body = await GetRequestTemplateAsync(nameof(ResumeUploadRequestAsync)).ConfigureAwait(false);
        var fileName = Path.GetFileName(fileSource.UploadFilePath);
        var replacedBody = body
            .Replace("{{FileName}}", fileName)
            .Replace("{{FileSize}}", stream.Length.ToString())
            .Replace("{{MimeType}}", GetMimeType(fileSource.UploadFilePath))
            .Replace("{{DataSourceName}}", fileName)
            .Replace("{{DataSourceGroupId}}", fileSource.DataSourceSetId.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };

        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
        var result = await response.Content
            .DeserializeToAnonymousTypeAsync(new
            {
                fileId = (Guid?)null,
                dataSourceId = (int?)null,
            }).ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");
        Guid fileId = result.fileId ?? throw new InvalidOperationException("FileId is not found in result.");
        int dataSourceId = result.dataSourceId ?? throw new InvalidOperationException("DataSourceId is not found in result.");

        fileSource.FileId = fileId;
        fileSource.DataSourceId = dataSourceId;

        await UploadFileChunkAsync(stream, fileSource, cancellationToken).ConfigureAwait(false);
    }

    private async Task UploadFileChunkAsync(Stream stream, FileSource fileSource, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);

        var requestUri = new Uri(serverUris.UploadApiUri, $"resume-upload");
        long from = 0;
        var length = stream.Length;
        const int ChunkSize = 102400000;
        var fileName = Path.GetFileName(fileSource.UploadFilePath);
        while (from < length)
        {
            var to = Math.Min(from + ChunkSize, length);

            // We are using WebRequest here because the HttpWebRequest does not allow setting Content-Range in request header
            var request = (HttpWebRequest)WebRequest.Create(requestUri);
            request.Method = "POST";
            request.KeepAlive = true;
            request.Headers.Add("Authorization", $"Bearer {Organization.AccessToken}");
            request.Headers.Add("Content-Range", $"bytes={from}-{to}/{length}");
            request.Headers.Add("x-file-id", fileSource.FileId.ToString());
            request.Headers.Add("x-dataSource-Id", fileSource.DataSourceId.ToString());

            var boundary = "---------------------------" + Guid.NewGuid();
            //boundary = "----WebKitFormBoundaryUv7SkXQvJbHbjWbd";
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            var startBoundary = "--" + boundary;
            var endBoundary = startBoundary + "--";

            var fileBuffer = new byte[to - from];
            await stream.ReadAsync(fileBuffer, 0, fileBuffer.Length);
            using (var requestStream = request.GetRequestStream())
            {
                async Task AddToRequest(byte[] buffer)
                {
                    await requestStream.WriteAsync(buffer, 0, buffer.Length);
                }

                // file chunk part
                var mimeType = "application/octet-stream"; //GetMimeType(fileSource.UploadFilePath);
                await AddToRequest(Encoding.ASCII.GetBytes(startBoundary + SystemEnvironment.NewLine));
                await AddToRequest(Encoding.UTF8.GetBytes($"Content-Disposition: form-data; name=\"{"chunk"}\"; filename=\"{fileName}\"{SystemEnvironment.NewLine}"));
                await AddToRequest(Encoding.ASCII.GetBytes($"Content-Type: {mimeType}{SystemEnvironment.NewLine}{SystemEnvironment.NewLine}"));
                await AddToRequest(fileBuffer);
                await AddToRequest(Encoding.ASCII.GetBytes(SystemEnvironment.NewLine));

                // fileId part
                await AddToRequest(Encoding.ASCII.GetBytes(startBoundary + SystemEnvironment.NewLine));
                await AddToRequest(Encoding.UTF8.GetBytes($"Content-Disposition: form-data; name=\"{"fileId"}\"{SystemEnvironment.NewLine}{SystemEnvironment.NewLine}"));
                await AddToRequest(Encoding.UTF8.GetBytes(fileSource.FileId.ToString()));
                await AddToRequest(Encoding.ASCII.GetBytes(SystemEnvironment.NewLine));
                await AddToRequest(Encoding.ASCII.GetBytes(endBoundary));
            }

            var response = await request.GetResponseAsync().ConfigureAwait(false);
            from = to;
        }
    }

    private async Task LegacyUploadFileAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);

        var requestUri = new Uri(serverUris.UploadApiUri, $"api/datasourceset/{fileSource.DataSourceSetId}/file");
        using (var multipartFormContent = new MultipartFormDataContent())
        {
            //Load the file and set the file's Content-Type header
            using var stream = GetUploadFileStream(fileSource);
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
            ".json" => ApplicationJsonContentType,
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
                Logger.LogWarning(ex, "Failed to get data set id. Will retry in {TimeBetweenRetries}", DelayBetweenGetDataSetIdRetries);
                await Task.Delay(DelayBetweenGetDataSetIdRetries, cancellationToken);
            }

        }
        throw new InvalidOperationException("Failed to get dataset id after multiple tries");
    }

    private async Task CommitAllDataSetAsync(CancellationToken cancellationToken)
    {
        foreach (var fileSource in FileSources)
        {
            await CommitDataSetAsync(fileSource, cancellationToken).ConfigureAwait(false);
            await Task.Delay(DelayBetweenDataSetCommits);
        }
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
            Content = new StringContent(replacedBody, Encoding.UTF8, ApplicationJsonContentType),
        };

        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
    }
}
