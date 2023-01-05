using System.Text.Json;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Services.ResultWriters;

internal class JsonFileResultWriter : IResultWriter
{
    public JsonFileResultWriter(ILogger<JsonFileResultWriter> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private ILogger<JsonFileResultWriter> Logger { get; }

    public virtual async Task ProcessAsync(string outputDirectoryPath, MultiIterationOperationResult result, CancellationToken cancellationToken)
    {
        try
        {
            var outputFilePath = Path.Combine(outputDirectoryPath, "results.json");
            await File.WriteAllTextAsync(outputFilePath, JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
            })).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error trying to save json result");
        }
    }
}
