using Docker.DotNet.Models;
using Docker.DotNet;

namespace CluedIn.QualityAssurance.Cli.Probes;

internal interface IStatsProbe
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task ProbeAsync(CancellationToken cancellationToken);
    Task<ICollection<StatsProbeResult>> GetProbeResultsAsync(CancellationToken cancellationToken);
}

internal class StatsProbe : IStatsProbe
{
    private List<StatsProbeResult> probeResults = new List<StatsProbeResult>();
    private DockerClient client;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        client = new DockerClientConfiguration().CreateClient();
        return Task.CompletedTask;
    }

    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var result = await ProbeInternalAsync(cancellationToken).ConfigureAwait(false);
        probeResults.Add(result);
    }

    public Task<ICollection<StatsProbeResult>> GetProbeResultsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<ICollection<StatsProbeResult>>(probeResults);
    }

    private async Task<StatsProbeResult> ProbeInternalAsync(CancellationToken cancellationToken)
    {
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters());
        var serverContainer = containers.SingleOrDefault(c => c.Image.Contains("cluedin-server"));
        if (serverContainer == null)
        {
            return new StatsProbeResult(DateTimeOffset.UtcNow, 0, 0);
        }

        int count = 0;
        double percentUsage = 0;
        ulong memoryUsageInBytes = 0;
        int targetCount = 2;
        using var cancellationTokenSource = new CancellationTokenSource();
        using var combinedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken);
        var progress = new Progress<ContainerStatsResponse>((statsResponse) =>
        {
            var usageDelta = statsResponse.CPUStats.CPUUsage.TotalUsage - statsResponse.PreCPUStats.CPUUsage.TotalUsage;
            var systemDelta = statsResponse.CPUStats.SystemUsage - statsResponse.PreCPUStats.SystemUsage;
            percentUsage = usageDelta / (double)systemDelta * statsResponse.CPUStats.OnlineCPUs * 100;
            memoryUsageInBytes = statsResponse.MemoryStats.Usage;

            count++;
            if (count >= targetCount)
            {
                cancellationTokenSource.Cancel();
            }
        });
        try
        {
            await client.Containers.GetContainerStatsAsync(serverContainer.ID, new ContainerStatsParameters
            {
                Stream = true,
            }, progress, combinedTokenSource.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException e)
        {
            if (count < targetCount)
            {
                throw;
            }
        }

        return new StatsProbeResult(DateTimeOffset.UtcNow, percentUsage, memoryUsageInBytes / 1024.0d / 1024.0d);
    }

}
public record StatsProbeResult(DateTimeOffset PolledAt, double CpuUsagePercentage, double MemoryUsageInMegabytes);
