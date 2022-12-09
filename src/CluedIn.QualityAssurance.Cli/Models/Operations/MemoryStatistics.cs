namespace CluedIn.QualityAssurance.Cli.Models.Operations;

internal class MemoryStatistics
{
    public float Before { get; set; }

    public float After { get; set; }

    public float Difference => After - Before;
}
