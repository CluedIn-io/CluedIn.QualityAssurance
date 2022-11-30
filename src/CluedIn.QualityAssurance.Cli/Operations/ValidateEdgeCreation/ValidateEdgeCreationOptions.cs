using CluedIn.QualityAssurance.Cli.Validators;
using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.VerifyEdges;

[Verb("validate-edge-creation", HelpText = "Validate edge creation by repeated importing of clues against multiple organizations")]
internal class ValidateEdgeCreationOptions
{
    [AbsoluteUri]
    [RoutableUri]
    [Option(Default = "http://localhost:9001/")]
    public string AuthApiUrl { get; set; }

    [AbsoluteUri]
    [RoutableUri]
    [Option(Default = "http://localhost:9007/")]
    public string PublicApiUrl { get; set; }

    [AbsoluteUri]
    [RoutableUri]
    [Option(Default = "http://localhost:9000/")]
    public string WebApiUrl { get; set; }

    [AbsoluteUri]
    [RoutableUri]
    [Option(Default = "http://localhost:15672/")]
    public string RabbitMQAdminUrl { get; set; }

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
    public string CluesFolder { get; set; }

    [Option(Default = 10)]
    public int Iterations { get; set; }

    [DirectoryExists]
    [Option(Required = true)]
    public string OutputDirectory { get; set; }

    [FileExists]
    [Option]
    public string ServerLogFile { get; set; }
}