using System.Diagnostics;
using System.Text.Json;

using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Models.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Services.Probes;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;

using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations.Monitoring;

internal class MonitorEnvironmentOperation : Operation<MonitorEnvironmentOptions>
{
    private static readonly JsonSerializerOptions ResultSerializerOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };
    public MonitorEnvironmentOperation(
        ILogger<MonitorEnvironmentOperation> logger,
        RabbitMqProbeService rabbitMqProbeService,
        IEnvironment environment,
        IProbeService probeService)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        RabbitMqProbeService = rabbitMqProbeService ?? throw new ArgumentNullException(nameof(rabbitMqProbeService));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        ProbeService = probeService ?? throw new ArgumentNullException(nameof(probeService));
    }
    private ILogger<MonitorEnvironmentOperation> Logger { get; }
    private RabbitMqProbeService RabbitMqProbeService { get; }
    public IEnvironment Environment { get; }
    private IProbeService ProbeService { get; }

    public override async Task ExecuteAsync(MonitorEnvironmentOptions options, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Begin monitoring environment.");
        if (options.ShouldCreateSubdirectoryForOutput)
        {
            options.OutputDirectory = Path.Combine(options.OutputDirectory, DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
            _ = Directory.CreateDirectory(options.OutputDirectory);
            Logger.LogInformation("Updating output directory to be {OutputDirectoryPath}' because create-subdirectory-for-output is set.", options.OutputDirectory);
        }
        await ProbeService.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await RabbitMqProbeService.InitializeAsync(cancellationToken).ConfigureAwait(false);
        using var ingestionCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var rabbitTask = RabbitMqProbeService.PollUntilCancellationAsync(ingestionCancellationTokenSource.Token);
        var probeTask = ProbeService.PollUntilCancellationAsync(ingestionCancellationTokenSource.Token);

        await Task.WhenAll(rabbitTask, probeTask, WriteOutputWhenCancelled(options, cancellationToken));

        async Task WriteOutputWhenCancelled(MonitorEnvironmentOptions options, CancellationToken cancellationToken)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            while (true)
            {
                if (File.Exists(Path.Combine(options.OutputDirectory, "stop.txt")) || cancellationToken.IsCancellationRequested)
                {
                    var outputFilePath = Path.Combine(options.OutputDirectory, "results.json");
                    Logger.LogInformation("End monitoring.");
                    ingestionCancellationTokenSource.Cancel();
                    var rabbitResult = await rabbitTask;
                    var probeResult = await probeTask;
                    await File.WriteAllTextAsync(
                        outputFilePath,
                        JsonSerializer.Serialize(new
                        {
                            Probes = probeResult,
                            Queues = rabbitResult,
                        }, ResultSerializerOptions));

                    Logger.LogInformation("End writing output to {OutputFilePath}.", outputFilePath);
                    break;
                }

                Logger.LogInformation("Monitoring is still in progress. Elapsed {Elapsed}.", stopwatch.Elapsed);
                await Task.Delay(5000);
            }
        }
    }
}


internal class RabbitMqProbeService
{
    private static readonly TimeSpan DelayBetweenQueuePolls = TimeSpan.FromSeconds(5);

    public RabbitMqProbeService(ILogger<RabbitMqProbeService> logger, IRabbitMQService rabbitMqService)
    {
        Logger = logger;
        RabbitMQService = rabbitMqService ?? throw new ArgumentNullException(nameof(rabbitMqService));
    }

    private ILogger<RabbitMqProbeService> Logger { get; }
    private IRabbitMQService RabbitMQService { get; }
    private List<RabbitMqProbeResult> ProbeResults { get; set; } = new();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ProbeResults = new List<RabbitMqProbeResult>();
        var result = await RabbitMQService.GetRabbitAllQueueInfoAsync(cancellationToken);
        ProbeResults.Add(new (result.First().PolledAt, result));
    }

    public async Task<IEnumerable<RabbitMqProbeResult>> PollUntilCancellationAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogInformation("Aborting polling because cancellation is requested.");

                return ProbeResults;
            }
            var result = await RabbitMQService.GetRabbitAllQueueInfoAsync(cancellationToken);
            ProbeResults.Add(new(result.First().PolledAt, result));
            await Task.Delay(DelayBetweenQueuePolls).ConfigureAwait(false);
        }

    }
}
internal record RabbitMqProbeResult(
    DateTimeOffset Time,
    IEnumerable<QueueInfo> QueueInfo);
