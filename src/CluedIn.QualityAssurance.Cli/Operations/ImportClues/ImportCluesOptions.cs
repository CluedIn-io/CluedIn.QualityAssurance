using CluedIn.QualityAssurance.Cli.Validators;
using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ImportClues;

[Verb("import-clues", HelpText = "Import serialized clues")]
internal class ImportCluesOptions
{
    [AbsoluteUri]
    [RoutableUri]
    [Option(HelpText = "The URL to the CluedIn AuthAPI", Required = true)]
    public string AuthApiUrl { get; set; }

    [Option(Required = true)]
    [DirectoryExists]
    public string CluesDirectory { get; set; }
}