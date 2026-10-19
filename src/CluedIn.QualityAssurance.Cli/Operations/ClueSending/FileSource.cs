namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal class FileSource : DataSource
{
    public Guid FileId { get; set; }

    public string UploadFilePath { get; set; }

    public bool IsExternalUploadFilePath { get; set; }
}

internal class DynamicDataSource : DataSource
{
}
