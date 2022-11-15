using CommandLine;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations.ImportClues;

[Verb("import-clues", HelpText = "Import serialized clues")]
internal class ImportCluesOperation : Operation<ImportCluesOptions>
{
    private readonly ILogger<ImportCluesOperation> _logger;

    public ImportCluesOperation(ILogger<ImportCluesOperation> logger)
    {
        _logger = logger;
    }

    public override Task ExecuteAsync(ImportCluesOptions options, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}