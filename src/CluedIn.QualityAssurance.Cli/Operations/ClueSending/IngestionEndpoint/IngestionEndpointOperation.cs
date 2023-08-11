using System.Globalization;
using System.Text;
using System.Text.Json;
using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Services.PostOperationActions;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Services.ResultWriters;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.IngestionEndpoint;

internal class IngestionEndpointOperation : FileSourceOperationBase<IngestionEndpointOptions>
{
    public IngestionEndpointOperation(
        ILogger<IngestionEndpointOperation> logger,
        IEnvironment testEnvironment,
        IEnumerable<IResultWriter> resultWriters,
        IRabbitMQCompletionChecker rabbitMqCompletionChecker,
        IEnumerable<IPostOperationAction> postOperationActions,
        IHttpClientFactory httpClientFactory)
        : base (logger, testEnvironment, resultWriters, rabbitMqCompletionChecker, postOperationActions, httpClientFactory)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private ILogger<IngestionEndpointOperation> Logger { get; }

    protected override Task ExecuteIngestionAsync(CancellationToken cancellationToken)
    {
        return StreamAllToIngestionEndpointAsync(cancellationToken);
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
            foreach(var fileSource in FileSources)
            {
                operations.Add(CreateSetupOperation(fileSource, CreateDataSourceSetAsync));
                operations.Add(CreateSetupOperation(fileSource, CreateDataSourceAsync));
                operations.Add(CreateSetupOperation(fileSource, CreateDataSetAsync));
                operations.Add(CreateSetupOperation(fileSource, SendSampleDataAsync));
                await AddMappingOperationsAsync(operations, fileSource, cancellationToken).ConfigureAwait(false);
                operations.Add(CreateSetupOperation(fileSource, ModifyDataSetAutoSubmitAsync));
            }
        }

        return operations;
    }

    private async Task StreamAllToIngestionEndpointAsync(CancellationToken cancellationToken)
    {
        foreach (var fileSource in FileSources)
        {
            await StreamToIngestionEndpointAsync(fileSource, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StreamToIngestionEndpointAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(fileSource.UploadFilePath);
        Logger.LogInformation("Begin streaming {FileName} to ingestion endpoint.", fileName);

        int BatchSize = Options.IngestionBatchSize;
        var fileStream = GetUploadFileStream(fileSource);
        using (var streamReader = new StreamReader(fileStream))
        using (var csv = new CsvReader(streamReader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
        }))
        {
            csv.Context.RegisterClassMap<MyClassWithDictionaryMapper>();

            var batch = new List<Dictionary<string, string>>(BatchSize);
            var totalSent = 0;
            try
            {
                var records = csv.GetRecords<CsvRow>();
                foreach (var currentRecord in records)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Logger.LogInformation("Aborting streaming because cancellation is requested.");
                        return;
                    }
                    batch.Add(currentRecord.Columns);
                    if (batch.Count == BatchSize)
                    {
                        var success = await SendBatchToIngestionEndpointAsync(fileSource, batch, cancellationToken).ConfigureAwait(false);
                        if (success)
                        {
                            totalSent += BatchSize;
                            Logger.LogDebug("Total rows sent {TotalSent}.", totalSent);
                        }
                        else
                        {
                            Logger.LogWarning("Failed to send batch. Ignoring batch.");
                        }
                        batch.Clear();
                        if (Options.IngestionRequestsDelayInMilliseconds > 0)
                        {
                            await Task.Delay(Options.IngestionRequestsDelayInMilliseconds, cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to send data.");
            }

            if (batch.Count > 0)
            {
                Logger.LogInformation("Sending last batch of rows.");
                var success = await SendBatchToIngestionEndpointAsync(fileSource, batch, cancellationToken).ConfigureAwait(false);
                if (success)
                {
                    totalSent += batch.Count;
                    Logger.LogDebug("Total rows sent {TotalSent}.", totalSent);
                }
                batch.Clear();
            }
            Logger.LogInformation("Finished streaming {FileName} to ingestion endpoint. Total Rows sent {TotalSent}", fileName, totalSent);
        }
    }

    private async Task<bool> SendBatchToIngestionEndpointAsync(FileSource fileSource, List<Dictionary<string, string>> batch, CancellationToken cancellationToken)
    {
        var totalRetries = 10;
        var delayBetweenRetries = TimeSpan.FromSeconds(2);
        for (var i = 0; i < totalRetries; ++i)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogInformation("Aborting streaming of batch because cancellation is requested.");
                return false;
            }

            try
            {
                var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
                var requestUri = new Uri(serverUris.UploadApiUri, $"api/endpoint/{fileSource.DataSetId}");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = new StringContent(JsonSerializer.Serialize(batch), Encoding.UTF8, "application/json"),
                };
                var response = await SendRequestAsync(requestMessage, cancellationToken, true, supressDebug: true).ConfigureAwait(false);
                var result = await response.Content
                    .DeserializeToAnonymousTypeAsync(new
                    {
                        success = (bool?)null,
                        received = (int?)null,
                        warning = (bool?)null,
                        error = (bool?)null,
                    })
                    .ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

                if (result.success.GetValueOrDefault())
                {
                    Logger.LogDebug("Successfully streamed batch of size {BatchSize}.", batch.Count);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error trying to send to ingestion endpoint. retrying in a while.");
                await Task.Delay(delayBetweenRetries, cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }


    private async Task<List<Dictionary<string, string>>> GetSampleDataAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var fileStream = GetUploadFileStream(fileSource);
        using (var streamReader = new StreamReader(fileStream))
        using (var csv = new CsvReader(streamReader, CultureInfo.InvariantCulture))
        {
            csv.Context.RegisterClassMap<MyClassWithDictionaryMapper>();
            var records = csv.GetRecords<CsvRow>();

            return records.Take(1).Select(row => row.Columns).ToList();
        }
    }

    private async Task CreateDataSourceAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(CreateDataSourceAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{UserId}}", Organization.UserId.ToString())
            .Replace("{{DataSourceSetId}}", fileSource.DataSourceSetId.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, "application/json"),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);

        var result = await response.Content
            .DeserializeToAnonymousTypeAsync(new
            {
                data = new
                {
                    inbound = new
                    {
                        createDataSource = new
                        {
                            id = (int?)null
                        },
                    },
                },
            })
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

        int? resultDataSourceId = result.data?.inbound?.createDataSource?.id ?? throw new InvalidOperationException("DataSourceSet is not found in result.");

        fileSource.DataSourceId = resultDataSourceId.Value;
    }

    private async Task CreateDataSetAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(CreateDataSetAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{UserId}}", Organization.UserId.ToString())
            .Replace("{{DataSourceId}}", fileSource.DataSourceId.ToString())
            .Replace("{{EntityType}}", fileSource.EntityType + "Dummy");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, "application/json"),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);

        var result = await response.Content
            .DeserializeToAnonymousTypeAsync(new
            {
                data = new
                {
                    inbound = new
                    {
                        createDataSets = new[]
                        {
                       new
                       {
                           id = (Guid?)null,
                       }
                        },
                    },
                },
            })
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Invalid result because it is empty.");

        var dataSetsArray = result.data?.inbound?.createDataSets;
        if (dataSetsArray == null || dataSetsArray.Length == 0 || dataSetsArray[0].id == null)
        {
            throw new InvalidOperationException("DataSourceSet is not found in result.");
        }

        fileSource.DataSetId = dataSetsArray[0].id.Value;
    }

    private async Task SendSampleDataAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var sampleData = await GetSampleDataAsync(fileSource, cancellationToken).ConfigureAwait(false);
        await SendBatchToIngestionEndpointAsync(fileSource, sampleData, cancellationToken).ConfigureAwait(false);
    }

    private async Task ModifyDataSetAutoSubmitAsync(FileSource fileSource, CancellationToken cancellationToken)
    {
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        var requestUri = serverUris.UiGraphqlUri;

        var body = await GetRequestTemplateAsync(nameof(ModifyDataSetAutoSubmitAsync)).ConfigureAwait(false);
        var replacedBody = body.Replace("{{DataSetId}}", fileSource.DataSetId.ToString());

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(replacedBody, Encoding.UTF8, "application/json"),
        };
        var response = await SendRequestAsync(requestMessage, cancellationToken, true).ConfigureAwait(false);
    }


    public class MyClassWithDictionaryMapper : ClassMap<CsvRow>
    {
        public MyClassWithDictionaryMapper()
        {

            Map(m => m.Columns).Convert
               (row => row.Row.HeaderRecord.Select
                (column => new { column, value = row.Row.GetField(column) })
                .ToDictionary(d => d.column, d => d.value)
                );
        }
    }
    public class CsvRow
    {
        public Dictionary<string, string> Columns { get; set; }
    }
}
