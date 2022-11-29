using CluedIn.QualityAssurance.Cli.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CluedIn.QualityAssurance.Cli.Operations.ExtractEdge;

internal class ExtractEdgeOperation : Operation<ExtractEdgeOptions>
{
    private readonly ILogger<ExtractEdgeOperation> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public ExtractEdgeOperation(ILogger<ExtractEdgeOperation> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }


    public override async Task ExecuteAsync(ExtractEdgeOptions options, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var edgeExporter = new EdgeExporter(_loggerFactory.CreateLogger<EdgeExporter>(), options.OutputDirectory);
        var organizationNames = options.OrganizationNames;
        foreach (var organizationName in organizationNames)
        {
            var mapping = options.Mappings
                .Select(keyValuePair => keyValuePair.Split("="))
                .ToDictionary(keyValuePair => keyValuePair.First(), keyValuePair => keyValuePair.Last());
            await edgeExporter.ExportEdges(organizationName, mapping).ConfigureAwait(false);
        }
        Console.WriteLine(
            JsonConvert.SerializeObject(
                edgeExporter.Summary.ToDictionary(x => x.Hash, x => x.NumberOfOccurances),
                Newtonsoft.Json.Formatting.Indented));
    }

}