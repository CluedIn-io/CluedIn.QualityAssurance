using CommandLine;
using CsvHelper;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using System.Globalization;
using System.Xml;

namespace CluedIn.QualityAssurance.Cli.Operations.VerifyEdge;

internal class VerifyEdgeOperation : Operation<VerifyEdgeOptions>
{
    private readonly ILogger<VerifyEdgeOperation> _logger;
    private const string Neo4jUserName = "neo4j";
    private const string Neo4jPassword = "password";

    public VerifyEdgeOperation(ILogger<VerifyEdgeOperation> logger)
    {
        _logger = logger;
    }

    public override async Task ExecuteAsync(VerifyEdgeOptions options, CancellationToken cancellationToken)
    {
        string cluesFolder = options.CluesFolder;
        string outputFilePath = options.OutputFile;
        string organizationName = options.OrganizationName;

        var edgeResult = new List<FileEdge>();
        var files = Directory.GetFiles(cluesFolder, "*.xml").OrderBy(x => x);

        var _driver = GraphDatabase.Driver(options.Neo4jUrl , AuthTokens.Basic(Neo4jUserName, Neo4jPassword));

        var start = DateTime.UtcNow;
        _logger.LogInformation("Begin checking result {StartTime}.", start);

        var totalNotMatching = 0;
        foreach (var file in files)
        {
            var contents = File.ReadAllText(file);

            var doc = new XmlDocument();
            doc.LoadXml(contents);

            var nodes = doc.SelectNodes("//edge");

            if (nodes == null)
            {
                continue;
            }

            foreach (var edge in nodes.OfType<XmlNode>())
            {
                var from = edge.Attributes["from"].Value.Replace("C:/", "/");
                var type = edge.Attributes["type"].Value;
                var to = edge.Attributes["to"].Value.Replace("C:/", "/");

                var query = @$"MATCH (n:{organizationName})-[e:`{type}`]->(m:{organizationName})
                WHERE '{from}{options.IdSuffix}' IN n.Codes
                AND '{to}{options.IdSuffix}' IN m.Codes
                RETURN COUNT(n) AS total";
                //                var query = @$"MATCH (n:{organizationName})-[e:`{type}`]->(m:{organizationName})
                //WHERE ANY(code in n.Codes WHERE code STARTS WITH '{from}{options.IdSuffix}')
                //AND ANY(code in m.Codes WHERE code STARTS WITH '{to}options.IdSuffix')
                //RETURN COUNT(n) AS total";
                using var session = _driver.AsyncSession();

                await session.ExecuteReadAsync(async tx =>
                {
                    var result = await tx.RunAsync(query, new Dictionary<string, object>()).ConfigureAwait(false);
                    var record = await result.SingleAsync().ConfigureAwait(false);
                    var total = record["total"].As<int>();

                    var edgeRecord = new FileEdge(file, from, type, to, total);
                    edgeResult.Add(edgeRecord);

                    if (total != 1)
                    {
                        _logger.LogWarning($"Edges found is not 1: {file} {from}-{type}->{to}. Total edge {total}");
                        totalNotMatching++;
                    }

                    if (edgeResult.Count % 100 == 0)
                    {
                        _logger.LogInformation("Total edges processed: {TotalEdges}", edgeResult.Count);
                    }
                }).ConfigureAwait(false);
            }
        }

        _logger.LogInformation(
            "Finished verifying edges. Total edges processed: {TotalEdges}. Total not matching: {TotalNotMatching}.",
            edgeResult.Count,
            totalNotMatching);
        var end = DateTime.UtcNow;
        var diff = end - start;
        _logger.LogInformation("End checking result {StartTime}. Time Taken: {SecondsTaken} seconds.", start, diff.TotalSeconds);

        using var writer = new StreamWriter(outputFilePath);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(edgeResult);
    }

    public record FileEdge(string file, string from, string type, string to, int total);
}