using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Validators;
using CommandLine;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations.VerifyEdge;

[Neo4jServerValid]
[Verb("verify-edge", HelpText = "Verify edges from clues directory with edges in Neo4j")]
internal class VerifyEdgeOptions : IOperationOptions, ILocalNeo4jOptions
{
    public string Neo4jBoltUri { get; set; }

    public string Neo4jUserName { get; set; }

    public string Neo4jUserPassword { get; set; }

    [Option("clues-directory", Required = true)]
    [DirectoryExists]
    public string CluesDirectory { get; set; }

    [Option("organization-name", Required = true)]
    public string OrganizationName { get; set; }

    [Option("output-file", Required = true)]
    public string OutputFile { get; set; }

    [Option("id-suffix", Required = true)]
    public string IdSuffix { get; set; }

    public LogLevel LogLevel { get; set; }
}