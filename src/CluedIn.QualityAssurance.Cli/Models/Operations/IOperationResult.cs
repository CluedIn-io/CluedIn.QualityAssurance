namespace CluedIn.QualityAssurance.Cli.Models.Operations;

internal interface IOperationResult
{
    // TODO: Capture errors instead of using boolean
    bool HasErrors { get; set; }

    DateTimeOffset StartTime { get; set; }

    DateTimeOffset? EndTime { get; set; }

    double TotalTimeInSeconds { get; }

    Dictionary<string, object> Output { get; set; }
}
