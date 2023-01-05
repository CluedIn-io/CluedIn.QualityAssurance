namespace CluedIn.QualityAssurance.Cli.Models.Operations;

internal class QueueStatistics
{
    public QueueCountStatistics Published { get; set; }

    public QueueCountStatistics Delivered { get; set; }
}
