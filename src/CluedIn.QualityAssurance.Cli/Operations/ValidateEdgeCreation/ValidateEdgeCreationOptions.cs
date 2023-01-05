using CluedIn.QualityAssurance.Cli.Operations.ClueSending;
using CluedIn.QualityAssurance.Cli.Operations.ClueSending.RawClues;
using CluedIn.QualityAssurance.Cli.Validators;
using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ValidateEdgeCreation;

[Verb("validate-edge-creation", HelpText = "Validate edge creation by repeated importing of clues against multiple organizations")]
internal class ValidateEdgeCreationOptions : ClueSendingOperationOptionsBase, IRawCluesOptions
{
    [Option("clues-directory", Required = true)]
    [DirectoryExists]
    public string CluesDirectory { get; set; }

    [FileExists]
    [Option("server-log-file")]
    public string ServerLogFile { get; set; }
}
