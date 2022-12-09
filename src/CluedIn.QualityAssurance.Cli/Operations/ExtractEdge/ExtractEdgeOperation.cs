using CluedIn.QualityAssurance.Cli.Services;
using Newtonsoft.Json;

namespace CluedIn.QualityAssurance.Cli.Operations.ExtractEdge;

internal class ExtractEdgeOperation : Operation<ExtractEdgeOptions>
{
    private readonly EdgeExporter _edgeExporter;

    public ExtractEdgeOperation(EdgeExporter edgeExporter)
    {
        _edgeExporter = edgeExporter ?? throw new ArgumentNullException(nameof(edgeExporter));
    }

    public override async Task ExecuteAsync(ExtractEdgeOptions options, CancellationToken cancellationToken)
    {
        _edgeExporter.Initialize(options.OutputDirectory, options.Neo4jBoltUri, options.Neo4jUserName, options.Neo4jUserPassword);
        var organizationNames = options.OrganizationNames;

        foreach (var organizationName in organizationNames)
        {
            var mapping = options.Mappings
                .Select(keyValuePair => keyValuePair.Split("="))
                .ToDictionary(keyValuePair => keyValuePair.First(), keyValuePair => keyValuePair.Last());
            await _edgeExporter.GetEdgeDetailsAsync(organizationName, mapping).ConfigureAwait(false);
        }
        Console.WriteLine(
            JsonConvert.SerializeObject(
                _edgeExporter.Summary.ToDictionary(x => x.Hash, x => x.NumberOfOccurances),
                Formatting.Indented));
        var allResult = new
        {
            Summary = _edgeExporter.Summary,
            OrganizationDetails = _edgeExporter.OrganizationResults,
        };

        await File.WriteAllTextAsync(
            Path.Combine(options.OutputDirectory, "results.json"),
            JsonConvert.SerializeObject(allResult, Formatting.Indented))
            .ConfigureAwait(false);
    }

}