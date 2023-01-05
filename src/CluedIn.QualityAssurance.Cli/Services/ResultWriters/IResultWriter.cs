using CluedIn.QualityAssurance.Cli.Models.Operations;

namespace CluedIn.QualityAssurance.Cli.Services.ResultWriters;

internal interface IResultWriter
{
    Task ProcessAsync(string outputDirectoryPath, MultiIterationOperationResult results, CancellationToken cancellationToken);
}
