namespace CluedIn.QualityAssurance.Cli.Environments;


internal record ProbeResult(
    DateTimeOffset Time,
    IEnumerable<ItemProbeResult> ItemProbeResults);

internal record ItemProbeResult(
    string ItemName,
    double CpuUsed,
    double MemoryUsedInMegabytes,
    double? CpuPercent = null,
    double? MemoryPercent = null);
