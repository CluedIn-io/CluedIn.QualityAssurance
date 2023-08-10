using Nest;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal class FileSource
{
    public int DataSourceSetId { get; set; }

    public int DataSourceId { get; set; }

    public Guid DataSetId { get; set; }

    public Guid FileId { get; set; }

    public int AnnotationId { get; set; }

    public string UploadFilePath { get; set; }

    public string VocabularyName { get; set; }

    public Guid VocabularyId { get; set; }

    public Dictionary<string, CustomVocabularyMappingEntry> CustomVocabulariesMapping { get; set; } =  new Dictionary<string, CustomVocabularyMappingEntry>();

    public string EntityType { get; set; }

    //public KeyValuePair<string, string> CustomEntityTypeMapping { get; set; }

    public Dictionary<string, string> CustomEntityTypesMapping { get; set; } = new Dictionary<string, string>();

    public bool IsExternalUploadFilePath { get; set; }


    public string EntityTypeRoute
    {
        get
        {
            return EntityType.ToLowerInvariant();
        }
    }
}

public class CustomVocabularyMappingEntry
{
    public string Name { get; set; }

    public Guid Id { get; set; }

    public Dictionary<string, CustomVocabularyKeyMappingEntry> KeysMapping { get; set; } = new Dictionary<string, CustomVocabularyKeyMappingEntry>();
}

public class CustomVocabularyKeyMappingEntry
{
    public string Name { get; set; }

    public Guid Id { get; set; }
}