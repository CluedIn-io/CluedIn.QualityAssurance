using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;

namespace CluedIn.QualityAssurance.Cli.Models.Operations;
internal class SingleIterationOperationResult : IOperationResult
{
    public bool HasErrors { get; set; }

    public bool HasTimedOut { get; set; }

    public DateTimeOffset StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public double TotalTimeInSeconds => EndTime == null ? 0 : (EndTime.Value - StartTime).TotalSeconds;

    public Dictionary<string, object> Output { get; set; } = new ();

    public Dictionary<string, QueueStatistics> TotalMessages { get; set; } = new ();

    public Dictionary<string, QueuePollingHistory> QueuePollingHistory { get; set; } = new ();

    public Organization Organization { get; set; } = new Organization();

    public MemoryStatistics MemoryStatistics { get; set; } = new MemoryStatistics();
}
