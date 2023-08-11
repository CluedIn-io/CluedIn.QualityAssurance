using CluedIn.QualityAssurance.Cli.Models.Operations;
using Microsoft.Extensions.Logging;
using RestSharp;
using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Services.PostOperationActions;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Services.ResultWriters;
using CluedIn.QualityAssurance.Cli.Services;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.RawClues;

internal class RawCluesOperation : RawCluesOperation<RawCluesOptions>
{
    public RawCluesOperation(
        ILogger<RawCluesOperation> logger,
        IEnvironment testEnvironment,
        IEnumerable<IResultWriter> resultWriters,
        IRabbitMQCompletionChecker rabbitMqCompletionChecker,
        IEnumerable<IPostOperationAction> postOperationActions,
        IHttpClientFactory httpClientFactory)
        : base(logger, testEnvironment, resultWriters, rabbitMqCompletionChecker, postOperationActions, httpClientFactory)
    {
    }
}

internal abstract class RawCluesOperation<TOptions> : ClueSendingOperation<TOptions>
    where TOptions : IRawCluesOptions
{
    public RawCluesOperation(
        ILogger<RawCluesOperation<TOptions>> logger,
        IEnvironment testEnvironment,
        IEnumerable<IResultWriter> resultWriters,
        IRabbitMQCompletionChecker rabbitMqCompletionChecker,
        IEnumerable<IPostOperationAction> postOperationActions,
        IHttpClientFactory httpClientFactory)
        : base(logger, testEnvironment, resultWriters, rabbitMqCompletionChecker, postOperationActions, httpClientFactory)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private ILogger<RawCluesOperation<TOptions>> Logger { get; }


    protected override Task ExecuteIngestionAsync(CancellationToken cancellationToken)
    {
        return StreamToRawClueEndpointAsync(cancellationToken);
    }

    protected override Task<IEnumerable<SetupOperation>> GetSetupOperationsAsync(bool isReingestion, CancellationToken cancellationToken)
    {
        var operations = new List<SetupOperation>();

        if (isReingestion)
        {
            operations.Add(new SetupOperation(LoginAsync));
        }
        else
        {
            operations.Add(new SetupOperation(CreateOrganizationAsync));
            operations.Add(new SetupOperation(LoginAsync));
        }

        return Task.FromResult<IEnumerable<SetupOperation>>(operations);
    }

    private async Task StreamToRawClueEndpointAsync(CancellationToken cancellationToken)
    {
        var client = new RestClient();
        var serverUris = await GetServerUris(cancellationToken).ConfigureAwait(false);
        Logger.LogInformation("Begin sending clues using files from '{CluesDirectory}'.", Options.CluesDirectory);

        var lastLogTime = DateTimeOffset.Now;
        int idx;
        var files = Directory.GetFiles(Options.CluesDirectory, "*.xml").OrderBy(x => x).ToArray();
        for (idx = 0; idx < files.Length; idx++)
        {
            var xml = await File.ReadAllTextAsync(files[idx]).ConfigureAwait(false);

            xml = CluesHelper.AppendTestRunSuffixToClueXml(xml, Organization.OrganizationId);


            var request = new RestRequest(new Uri(serverUris.PublicApiUri, "api/v2/clue"), Method.Post);
            request.RequestFormat = DataFormat.Json;
            request.AddParameter("save", "true", ParameterType.QueryString);
            request.AddParameter("bearer", Organization.AccessToken, ParameterType.QueryString);
            request.AddParameter("text/xml", xml, ParameterType.RequestBody);


            var response = await client.ExecuteAsync(request);

            if (DateTimeOffset.Now.Subtract(lastLogTime).TotalSeconds > 5)
            {
                Logger.LogInformation($"Sent {idx}/{files.Length} clues");
                lastLogTime = DateTimeOffset.Now;
            }

            if (cancellationToken.IsCancellationRequested)
                return;
        }
        Logger.LogInformation("End sending clues using {TotalFiles} files from '{CluesDirectory}'.", idx, Options.CluesDirectory);
    }
}
