using CluedIn.QualityAssurance.Cli.Validators;
using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ExtractEdge;

[Verb("extract-edge", HelpText = "Extract edges from organizations")]
internal class ExtractEdgeOptions
{
    [AbsoluteUri]
    [RoutableUri]
    [Option(Default = "bolt://localhost:7687")]
    public string Neo4jUrl { get; set; }

    [Option(Default = "neo4j")]
    public string Neo4jUsername { get; set; }

    [Option(Default = "password")]
    public string Neo4jPassword { get; set; }

    [Option(Required = true)]
    [DirectoryExists]
    public string OutputDirectory { get; set; }

    [Option(Required = true, Separator =',')]
    public IEnumerable<string> OrganizationNames { get; set; }

    [Option(Required = true, Separator = ',')]
    [KeyValuePairs]
    public IEnumerable<string> Mappings { get; set; }
}