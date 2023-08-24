namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal abstract class FileSourceOperationOptionsBase : ClueSendingOperationOptionsBase, IFileSourceOperationOptions
{
    public string InputFilePath { get; set; }

    public string InputDirectoryPath { get; set; }

    public int DelayAfterVocabularyKeyCreationInMilliseconds { get; set; }
}
