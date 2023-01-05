using System.Text.Json;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Services.ResultWriters;

internal class HtmlFileResultWriter : IResultWriter
{
    public HtmlFileResultWriter(ILogger<HtmlFileResultWriter> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private ILogger<HtmlFileResultWriter> Logger { get; }

    public virtual async Task ProcessAsync(string outputDirectoryPath, MultiIterationOperationResult result, CancellationToken cancellationToken)
    {
        try
        {
            var template = await GetTemplateAsync().ConfigureAwait(false);
            var output = template.Replace("{{JSON_DATA}}", JsonSerializer.Serialize(result));
            var outputFilePath = Path.Combine(outputDirectoryPath, "results.html");
            await File.WriteAllTextAsync(outputFilePath, output).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error trying to save json result");
        }
    }

    private async Task<string> GetTemplateAsync()
    {
        var currentType = GetType();
        var assembly = currentType.Assembly;
        var resourceName = $"{currentType.Namespace}.Data.HtmlFileResultWriter.index.html";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Failed to read manifest resource stream for '{resourceName}'");
        }
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }
}
