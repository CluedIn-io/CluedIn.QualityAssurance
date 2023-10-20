namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal abstract class FileSourceOperationOptions : ClueSendingOperationOptions, IFileSourceOperationOptions
{
    public string InputFilePath { get; set; }

    public string InputDirectoryPath { get; set; }

    public int DelayAfterVocabularyKeyCreationInMilliseconds { get; set; }
}
