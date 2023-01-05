namespace CluedIn.QualityAssurance.Cli.Models.Operations;

internal class QueueCountStatistics
{
    public uint Before { get; set; }

    public uint After { get; set; }

    public uint Difference => After - Before;
}
