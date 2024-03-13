using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.FileUpload;

internal interface IFileUploadOptions : IFileSourceOperationOptions
{
    [Option("use-legacy-file-upload", Default = false, Required = false, HelpText = "Use legacy file upload (for CluedIn prior to release-3.7.0)")]
    bool UseLegacyFileUpload { get; set; }
}
