using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal interface IFileSourceOperationOptions : IClueSendingOperationOptions
{
    [Option('i', "input-file", Default = "Movies-5000.csv", Required = false, HelpText = "The file path for the test data. If the file exists, it will be used. Otherwise it will try to find the test file embedded in the tool.")]
    string InputFilePath { get; set; }
}
