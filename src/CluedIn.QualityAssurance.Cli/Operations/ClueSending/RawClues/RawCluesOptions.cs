using CluedIn.QualityAssurance.Cli.Validators;
using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.RawClues;

[Verb("raw-clues", HelpText = "Send raw clues from a folder.")]
internal class RawCluesOptions : ClueSendingOperationOptions, IRawCluesOptions
{
    [Option("clues-directory", Required = true)]
    [DirectoryExists]
    public string CluesDirectory { get; set; }
}
