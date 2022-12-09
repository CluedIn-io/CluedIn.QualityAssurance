using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Validators;
using CommandLine;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations.ExtractEdge;

[Neo4jServerValid]
[Verb("extract-edge", HelpText = "Extract edges from organizations")]
internal class ExtractEdgeOptions : IOperationOptions, ILocalNeo4jOptions
{
    #region Neo4j
    public string Neo4jBoltUri { get; set; }

    public string Neo4jUserName { get; set; }

    public string Neo4jUserPassword { get; set; }
    #endregion

    [Option("output-directory", Required = true)]
    [DirectoryExists]
    public string OutputDirectory { get; set; }

    [Option("organization-names", Required = true, Separator =',')]
    public IEnumerable<string> OrganizationNames { get; set; }

    [Option("mappings", Required = true, Separator = ',')]
    [KeyValuePairs]
    public IEnumerable<string> Mappings { get; set; }

    public LogLevel LogLevel { get; set; }
}