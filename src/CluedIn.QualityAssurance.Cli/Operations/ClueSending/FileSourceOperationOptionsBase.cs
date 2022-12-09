namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal abstract class FileSourceOperationOptionsBase : ClueSendingOperationOptionsBase, IFileSourceOperationOptions
{
    public string InputFilePath { get; set; }
}
