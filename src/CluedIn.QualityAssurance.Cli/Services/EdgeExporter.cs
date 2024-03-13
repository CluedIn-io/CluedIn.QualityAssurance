using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web;
using CsvHelper;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace CluedIn.QualityAssurance.Cli.Services;

internal class EdgeExporter
{
    private readonly ILogger<EdgeExporter> _logger;
    private string? _outputDirectory;
    private string? _neo4jUserName;
    private string? _neo4jPassword;
    private string? _neo4jUri;

    internal List<OrganizationEdgeDetails> OrganizationResults { get; set; } = new();
    internal EdgeSummary[] Summary { get; private set; }

    public EdgeExporter(ILogger<EdgeExporter>? logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public class OrganizationEdgeDetails
    {
        public string OrganizationId { get; set; }
        public string EdgeHash { get; set; }
        public int NumberOfEdges { get; set; }
        public TimeSpan TimeToProcess { get; set; }
    }

    public void Initialize(string outputDirectory, string neo4jUri, string neo4jUserName, string neo4jPassword)
    {
        if (string.IsNullOrEmpty(outputDirectory))
        {
            throw new ArgumentException($"'{nameof(outputDirectory)}' cannot be null or empty.", nameof(outputDirectory));
        }

        if (string.IsNullOrWhiteSpace(neo4jUri))
        {
            throw new ArgumentException($"'{nameof(neo4jUri)}' cannot be null or whitespace.", nameof(neo4jUri));
        }

        if (string.IsNullOrWhiteSpace(neo4jUserName))
        {
            throw new ArgumentException($"'{nameof(neo4jUserName)}' cannot be null or whitespace.", nameof(neo4jUserName));
        }

        if (string.IsNullOrWhiteSpace(neo4jPassword))
        {
            throw new ArgumentException($"'{nameof(neo4jPassword)}' cannot be null or whitespace.", nameof(neo4jPassword));
        }

        _outputDirectory = outputDirectory;
        _neo4jUri = neo4jUri;
        _neo4jUserName = neo4jUserName;
        _neo4jPassword = neo4jPassword;
    }

    public async Task<OrganizationEdgeDetails> GetEdgeDetailsAsync(string organizationName, Dictionary<string, string> mapping)
    {
        var organizationId = await GetOrganizationIdFromName(organizationName).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            throw new InvalidOperationException($"Cannot find OrganizationId from name '{organizationName}'.");
        }
        return await GetEdgeDetailsAsync(organizationName, organizationId, default, mapping).ConfigureAwait(false);
    }

    public async Task<OrganizationEdgeDetails> GetEdgeDetailsAsync(string organizationName, string organizationId, TimeSpan timeTaken, Dictionary<string, string> mapping)
    {
        var edges = await GetEdgesFromNeo(organizationName, mapping).ConfigureAwait(false);

        var outputCsvFileName = Path.Combine(_outputDirectory, $"edges-{organizationName}.csv");
        await ExportToCsv(outputCsvFileName, edges).ConfigureAwait(false);

        var sHash = await CalculateFileHash(outputCsvFileName).ConfigureAwait(false);
        var outputHashCsvFileName = Path.Combine(_outputDirectory, $"edges-{sHash}.csv");
        CreateHashCsvIfNotExists(outputCsvFileName, outputHashCsvFileName);

        var details = new OrganizationEdgeDetails
        {
            OrganizationId = organizationId,
            EdgeHash = sHash,
            NumberOfEdges = edges.Count,
            TimeToProcess = timeTaken,
        };
        OrganizationResults.Add(details);

        Summary = (from r in OrganizationResults
                       group r by r.EdgeHash
                       into g
                       select new EdgeSummary(
                           g.Key,
                           g.Count(),
                           TimeSpan.FromTicks(g.Select(x => x.TimeToProcess.Ticks).Sum() / g.Count()),
                           g.Select(x => x.NumberOfEdges).Sum() / (double)g.Count() // all counts should be the same for a given hash, lets average however just incase
                       )).ToArray();
        return details;
    }

    private async Task<string?> GetOrganizationIdFromName(string organizationName)
    {
        var driver = CreateDriver();
        var query = $@"MATCH (n:`/Organization`)
WHERE n.Name = '{organizationName}'
RETURN n.Organization AS OrganizationId LIMIT 1";
        using var session = driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var result = await tx.RunAsync(query, new Dictionary<string, object>()).ConfigureAwait(false);
            var record = await result.SingleAsync();

            return record?.Values["OrganizationId"]?.ToString();
        });
    }

    private void CreateHashCsvIfNotExists(string outputCsvFileName, string outputHashCsvFileName)
    {
        if (!File.Exists(outputHashCsvFileName))
        {
            File.Copy(outputCsvFileName, outputHashCsvFileName);
        }
    }

    private async Task ExportToCsv(string outputCsvFileName, IEnumerable<Result> edges)
    {
        using var writer = new StreamWriter(outputCsvFileName);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        await csv.WriteRecordsAsync(edges);
    }

    private async Task<string> CalculateFileHash(string fileName)
    {
        byte[] hash;
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(fileName);
        hash = await md5.ComputeHashAsync(stream).ConfigureAwait(false);

        return HttpUtility.UrlEncode(Convert.ToBase64String(hash));
    }

    private async Task<ICollection<Result>> GetEdgesFromNeo(string organizationName, Dictionary<string, string> mapping)
    {
        _logger.LogInformation("Fetching edge data from neo");
        var driver = CreateDriver();

        var query = $@"MATCH (n)-[e]->(r) 
WHERE type(e) <> '/Code'
AND type(e) <> '/DiscoveredAt'
AND type(e) <> '/ModifiedAt'
AND n.`Attribute-origin` STARTS WITH '{organizationName}'
RETURN n.Codes AS source, type(e) AS type, r.Codes AS destination
ORDER BY source, type, destination";

        using var session = driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var result = await tx.RunAsync(query, new Dictionary<string, object>()).ConfigureAwait(false);
            var resultList = await result.ToListAsync();
            var resultObjectList = resultList.Select(currentResult => new Result(
                    RemapCode(SortedHead((List<object>)currentResult.Values["source"]), mapping),
                    currentResult.Values["type"].ToString(),
                    RemapCode(SortedHead((List<object>)currentResult.Values["destination"]), mapping)))
                .Where(x => !x.source.Contains("#CluedIn"))
                .Distinct()
                .OrderBy(x => x.source)
                .ThenBy(x => x.type)
                .ThenBy(x => x.destination)
                .ToArray();


            return resultObjectList;
        });
    }

    private IDriver CreateDriver()
    {
        return GraphDatabase.Driver(_neo4jUri, AuthTokens.Basic(_neo4jUserName, _neo4jPassword));
    }

    private static string RemapCode(string s, Dictionary<string, string> mapping)
    {
        var replaced = s;
        foreach(var current in mapping)
        {
            replaced = Regex.Replace(replaced, current.Key, current.Value);
        }

        return replaced;
    }

    private static string SortedHead(object o)
    {
        var list = ((List<object>)o).Select(x => x.ToString());

        return list.OrderBy(x => x).First();
    }

    internal record Result(string source, string type, string destination);
    internal record EdgeSummary(string Hash, int NumberOfOccurances, TimeSpan AverageProcessingTime, double NumberOfEdges);
}

