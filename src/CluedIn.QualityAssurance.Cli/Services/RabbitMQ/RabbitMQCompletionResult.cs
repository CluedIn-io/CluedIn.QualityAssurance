using CluedIn.QualityAssurance.Cli.Models.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Probes;

namespace CluedIn.QualityAssurance.Cli.Services.RabbitMQ;

internal record QueuePollingHistory(string QueueName, string QueueShortName, List<QueueInfo> HistoricalQueueInfo);

internal record RabbitMQCompletionResult(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    Dictionary<string, QueuePollingHistory> QueuePollingHistory,
    ICollection<StatsProbeResult> StatsProbeResults);
