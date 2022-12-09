using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.FileUpload;

[Verb("file-upload", HelpText = "Perform test using file upload.")]
internal class FileUploadOptions : FileSourceOperationOptionsBase, IFileUploadOptions
{
}
