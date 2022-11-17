using CluedIn.QualityAssurance.Cli.Validators;
using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.VerifyEdge;

[Verb("verify-edge", HelpText = "Verify edges from clues folder with edges in Neo4j")]
internal class VerifyEdgeOptions
{
    [AbsoluteUri]
    [RoutableUri]
    [Option(Default = "bolt://localhost:7687")]
    public string Neo4jUrl { get; set; }

    [Option(Required = true)]
    [DirectoryExists]
    public string CluesFolder { get; set; }

    [Option(Required = true)]
    public string OrganizationName { get; set; }

    [Option(Required = true)]
    public string OutputFile { get; set; }

    [Option(Required = true)]
    public string IdSuffix { get; set; }
}