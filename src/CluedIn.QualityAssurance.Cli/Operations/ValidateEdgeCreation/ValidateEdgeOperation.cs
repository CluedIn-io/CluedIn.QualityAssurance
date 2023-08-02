using CluedIn.QualityAssurance.Cli.Models.Operations;
using CluedIn.QualityAssurance.Cli.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Services.PostOperationActions;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Services.ResultWriters;
using CluedIn.QualityAssurance.Cli.Operations.ClueSending.RawClues;

namespace CluedIn.QualityAssurance.Cli.Operations.ValidateEdgeCreation;

internal class ValidateEdgeOperation : RawCluesOperation<ValidateEdgeCreationOptions>
{
    public ValidateEdgeOperation(
        ILogger<ValidateEdgeOperation> logger,
        IEnvironment testEnvironment,
        IEnumerable<IResultWriter> resultWriters,
        IRabbitMQCompletionChecker rabbitMqCompletionChecker,
        IEnumerable<IPostOperationAction> postOperationActions,
        IHttpClientFactory httpClientFactory,
        EdgeExporter edgeExporter)
        : base(logger, testEnvironment, resultWriters, rabbitMqCompletionChecker, postOperationActions, httpClientFactory)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        EdgeExporter = edgeExporter ?? throw new ArgumentNullException(nameof(edgeExporter));
    }
    private ILogger<ValidateEdgeOperation> Logger { get; }
    private EdgeExporter EdgeExporter { get; }
    private long ServerLogOffset { get; set; }

    public override Task ExecuteAsync(ValidateEdgeCreationOptions options, CancellationToken cancellationToken)
    {
        SetAndCreateOutputDirectory(options);
        return base.ExecuteAsync(options, cancellationToken);
    }

    private void SetAndCreateOutputDirectory(ValidateEdgeCreationOptions options)
    {
        var nowInTicks = DateTime.Now.Ticks;
        options.OutputDirectory = Path.Combine(options.OutputDirectory, nowInTicks.ToString());

        Directory.CreateDirectory(options.OutputDirectory);
    }

    protected override async Task ExecuteIngestionAsync(CancellationToken cancellationToken)
    {
        long serverLogOffset = 0;

        if (!string.IsNullOrWhiteSpace(Options.ServerLogFile))
        {
            using (var fs = File.Open(Options.ServerLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                serverLogOffset = fs.Length;
            }
        }
        ServerLogOffset = serverLogOffset;

        await base.ExecuteIngestionAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ProcessResultAsync(MultiIterationOperationResult results, CancellationToken cancellationToken)
    {
        var org = Organization.ClientId;
        var mapping = new Dictionary<string, string>
        {
            [$"^{Organization.ClientId}~"] = "",
            [$"{CluesHelper.GetTestRunSuffix(Organization.OrganizationId)}"] = ""
        };
        var currentResult = results.IterationResults.Last();
        var edgeDetails = await EdgeExporter.GetEdgeDetailsAsync(org, Organization.OrganizationId.ToString(), currentResult.EndTime.Value - currentResult.StartTime, mapping).ConfigureAwait(false);

        currentResult.Output.Add("EdgeHash", edgeDetails.EdgeHash);
        currentResult.Output.Add("NumberOfEdges", edgeDetails.NumberOfEdges);


        if (!string.IsNullOrWhiteSpace(Options.ServerLogFile))
        {
            using (var fs = File.Open(Options.ServerLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(ServerLogOffset, SeekOrigin.Begin);

                using (var outputFs = File.OpenWrite($"{Options.OutputDirectory}\\server-log-{org}.txt"))
                {
                    fs.CopyTo(outputFs);
                }
            }
        }

        var json = JsonConvert.SerializeObject(
                EdgeExporter.Summary.ToDictionary(x => x.Hash, x => x.NumberOfOccurances),
                Formatting.Indented);

        if (!results.Output.ContainsKey("EdgeSummary"))
        {
            results.Output.Add("EdgeSummary", EdgeExporter.Summary);
        }
        else
        {
            results.Output["EdgeSummary"] = EdgeExporter.Summary;
        }
        Console.WriteLine(json);
        await base.ProcessResultAsync(results, cancellationToken).ConfigureAwait(false);
    }

    private async Task InitializeEdgeExporterAsync(CancellationToken cancellationToken)
    {
        var neo4jConnection = await Environment.GetNeo4jConnectionInfoAsync(cancellationToken).ConfigureAwait(false);
        EdgeExporter.Initialize(Options.OutputDirectory, neo4jConnection.BoltUri.ToString(), neo4jConnection.UserName, neo4jConnection.Password);
    }

    protected override async Task<IEnumerable<SetupOperation>> GetSetupOperationsAsync(bool isReingestion, CancellationToken cancellationToken)
    {
        var operations = new List<SetupOperation>
        {
            new SetupOperation(InitializeEdgeExporterAsync)
        };

        operations.AddRange(await base.GetSetupOperationsAsync(isReingestion, cancellationToken).ConfigureAwait(false));

        return operations;
    }
    protected override Task CreateOperationData(int iterationNumber)
    {
        var nowInTicks = DateTime.Now.Ticks;
        var clientId = $"myorg{nowInTicks}";
        Organization = new Organization
        {
            ClientId = clientId,
            Password = Options.Password,
            UserName = $"{Options.UserName}@{clientId}.com",
            EmailDomain = $"{clientId}.com",
        };
        return Task.CompletedTask;
    }
}
