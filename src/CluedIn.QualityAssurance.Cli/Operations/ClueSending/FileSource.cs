namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal class FileSource
{
    public int DataSourceSetId { get; set; }

    public int DataSourceId { get; set; }

    public Guid DataSetId { get; set; }

    public int AnnotationId { get; set; }

    public string UploadFilePath { get; set; }

    public string VocabularyKey { get; set; }

    public string EntityType { get; set; }

    public bool IsExternalUploadFilePath { get; set; }
}
