using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using CluedIn.QualityAssurance.Cli.Operations.ClueSending.RawClues;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using System.Xml;

namespace CluedIn.QualityAssurance.Cli.Services.PostOperationActions;

internal class CodesAssertionAction : IPostOperationAction
{
    public CodesAssertionAction(ILogger<CodesAssertionAction> logger, IEnvironment testEnvironment, IRawCluesOptions rawCluesOptions)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TestEnvironment = testEnvironment ?? throw new ArgumentNullException(nameof(testEnvironment));
        RawCluesOptions = rawCluesOptions ?? throw new ArgumentNullException(nameof(rawCluesOptions));
    }

    private ILogger<CodesAssertionAction> Logger { get; }
    private IEnvironment TestEnvironment { get; }
    private IRawCluesOptions RawCluesOptions { get; }

    public async Task ExecuteAsync(SingleIterationOperationResult result, CancellationToken cancellationToken)
    {
        string cluesDirectory = RawCluesOptions.CluesDirectory;
        string organizationName = result.Organization.ClientId;

        var files = Directory.GetFiles(cluesDirectory, "*.xml").OrderBy(x => x);
        var neo4jConnectionInfo = await TestEnvironment.GetNeo4jConnectionInfoAsync(cancellationToken);
        var driver = GraphDatabase.Driver(neo4jConnectionInfo.BoltUri, AuthTokens.Basic(neo4jConnectionInfo.UserName, neo4jConnectionInfo.Password));
        var start = DateTime.UtcNow;
        Logger.LogInformation("Begin checking result {StartTime}.", start);

        var totalNotMatching = 0;
        var idSuffix = CluesHelper.GetTestRunSuffix(result.Organization.OrganizationId);
        foreach (var file in files)
        {
            var contents = await File.ReadAllTextAsync(file).ConfigureAwait(false);

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(contents);

            var codeNodes = xmlDoc.SelectNodes("//codes/value");

            if (codeNodes == null)
            {
                continue;
            }

            var codes = codeNodes.OfType<XmlNode>().Select(node => node.InnerText).ToHashSet();

            var origins = xmlDoc.SelectNodes("//*[@origin]");

            if (origins != null)
            {
                codes.UnionWith(origins.OfType<XmlNode>().Select(node => node.Attributes["origin"].Value));
            }

            var distinctCodes = codes.Distinct();
            var appendedDistinctCodes = distinctCodes.Select(code => code + idSuffix);

            var query = $@"MATCH (n:{organizationName})-[r:`/Code`]->(m:`/EntityCode`)
                    WHERE ANY(code IN n.Codes WHERE code IN $codes)
                    RETURN n.Id AS Id, n.Codes AS EntityCodes, COLLECT(m.Code) AS NodeCodes";
            using var session = driver.AsyncSession();

            await session.ExecuteReadAsync(async tx =>
            {
                var result = await tx.RunAsync(query, new Dictionary<string, object>()
                {
                    ["codes"] = appendedDistinctCodes.ToList(),
                }).ConfigureAwait(false);
                var records = await result.ToListAsync(x => new Entity
                {
                    Id = x[nameof(Entity.Id)].As<string>(),
                    EntityCodes = x[nameof(Entity.EntityCodes)].As<List<string>>(),
                    NodeCodes = x[nameof(Entity.NodeCodes)].As<List<string>>(),
                }).ConfigureAwait(false);

                //var allIds = records.Select(record => record.Id).ToHashSet();

                //if (allIds.Count() > 1)
                //{
                //    Logger.LogWarning("More than one entity found? ");
                //}

                var hasAllDistinctCodes = records.Any(
                    record =>
                    {
                        var hashSet = record.EntityCodes.ToHashSet();
                        return appendedDistinctCodes.All(code => hashSet.Contains(code));
                    });


                if (!hasAllDistinctCodes)
                {
                    Logger.LogWarning($"Entity containing '{string.Join(',', appendedDistinctCodes)}' was not found.");
                    totalNotMatching++;
                }

            }).ConfigureAwait(false);
        }

        result.Output.Add("MissingCodesCount", totalNotMatching);
    }

    private class Entity
    {
        public string Id { get; set; }

        public List<string> EntityCodes { get; set; }

        public List<string> NodeCodes { get; set; }
    }
}
