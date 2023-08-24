using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal interface IFileSourceOperationOptions : IClueSendingOperationOptions
{
    [Option('i', "input-file", Default = "Movies-5000.csv", Required = false, HelpText = "The file path for the test data. If the file exists, it will be used. Otherwise it will try to find the test file embedded in the tool.")]
    string InputFilePath { get; set; }

    [Option("input-directory", Default = null, Required = false, HelpText = "The directory path for input files.")]
    string InputDirectoryPath { get; set; }

    [Option("milliseconds-after-vocabulary-key-creation", Default = 300, Required = false, HelpText = "Delay after vocabulary key creation, in milliseconds.")]
    int DelayAfterVocabularyKeyCreationInMilliseconds { get; set; }
}
