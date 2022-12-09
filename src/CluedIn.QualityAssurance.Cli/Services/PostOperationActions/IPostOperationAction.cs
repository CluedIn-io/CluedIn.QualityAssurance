using CluedIn.QualityAssurance.Cli.Models.Operations;

namespace CluedIn.QualityAssurance.Cli.Services.PostOperationActions;

internal interface IPostOperationAction
{
    Task ExecuteAsync(SingleIterationOperationResult currentIterationResult, CancellationToken cancellationToken);
    Task ExecuteAsync(MultiIterationOperationResult overallResult, CancellationToken cancellationToken) => Task.CompletedTask;
}
