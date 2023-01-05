namespace CluedIn.QualityAssurance.Cli.Models.Operations;

internal class MultiIterationOperationResult : IMultiIterationOperationResult<SingleIterationOperationResult>
{
    public bool HasErrors { get; set; }

    public DateTimeOffset StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public double TotalTimeInSeconds => EndTime == null ? 0 : (EndTime.Value - StartTime).TotalSeconds;

    public Dictionary<string, object> Output { get; set; } = new();

    public ICollection<SingleIterationOperationResult> IterationResults { get; set; } = new List<SingleIterationOperationResult>();
}