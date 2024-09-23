using CluedIn.QualityAssurance.Cli.Environments;

using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Services.Probes;

internal interface IProbeService
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IEnumerable<ProbeResult>> PollUntilCancellationAsync(CancellationToken cancellationToken);
}

internal class ProbeService : IProbeService
{
    private static readonly TimeSpan DelayBetweenQueuePolls = TimeSpan.FromSeconds(3);

    public ProbeService(ILogger<ProbeService> logger, IEnvironment environment)
    {
        Logger = logger;
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    private ILogger<ProbeService> Logger { get; }
    private IEnvironment Environment { get; }
    private List<ProbeResult> ProbeResults { get; set; } = new();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ProbeResults = new List<ProbeResult>();
        var result = await Environment.ProbeAsync(cancellationToken);
        ProbeResults.Add(result);
    }

    public async Task<IEnumerable<ProbeResult>> PollUntilCancellationAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogInformation("Aborting polling because cancellation is requested.");

                return ProbeResults;
            }
            var result = await Environment.ProbeAsync(cancellationToken);
            ProbeResults.Add(result);
            await Task.Delay(DelayBetweenQueuePolls).ConfigureAwait(false);
        }

    }
}
