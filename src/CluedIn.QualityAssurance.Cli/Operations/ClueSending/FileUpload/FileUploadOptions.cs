using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.FileUpload;

[Verb("file-upload", HelpText = "Perform test using file upload.")]
internal class FileUploadOptions : FileSourceOperationOptions, IFileUploadOptions
{
    public bool UseLegacyFileUpload { get; set; }
}
