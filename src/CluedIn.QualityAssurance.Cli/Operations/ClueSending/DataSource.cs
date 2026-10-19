namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal class DataSource
{
    public int DataSourceSetId { get; set; }

    public int DataSourceId { get; set; }

    public Guid DataSetId { get; set; }

    public int AnnotationId { get; set; }

    public string VocabularyName { get; set; }

    public Guid VocabularyId { get; set; }

    public Dictionary<string, CustomVocabularyMappingEntry> CustomVocabulariesMapping { get; set; } =  new();

    public string EntityType { get; set; }

    public Dictionary<string, string> CustomEntityTypesMapping { get; set; } = new();

    public string EntityTypeRoute => EntityType.ToLowerInvariant();

    public Dictionary<string, string> VocabularyKeyToAnnotationKeyMapping { get; set; } = new();
}

public record CustomVocabularyMappingEntry(string Name, Guid Id)
{
    public Dictionary<string, CustomVocabularyKeyMappingEntry> KeysMapping { get; init; } = new();
}

public record CustomVocabularyKeyMappingEntry(string Name, Guid Id);
