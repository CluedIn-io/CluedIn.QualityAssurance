namespace CluedIn.QualityAssurance.Cli.Models.Operations;

internal interface IMultiIterationOperationResult<TIterationResult> : IOperationResult
{
    public ICollection<TIterationResult> IterationResults { get; set; }
}
