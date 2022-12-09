using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace CluedIn.QualityAssurance.Cli.Services.PostOperationActions;

internal class EntitiesCountAssertionAction : IPostOperationAction
{
    public EntitiesCountAssertionAction(ILogger<EntitiesCountAssertionAction> logger, IEnvironment testEnvironment)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TestEnvironment = testEnvironment ?? throw new ArgumentNullException(nameof(testEnvironment));
    }

    private ILogger<EntitiesCountAssertionAction> Logger { get; }
    private IEnvironment TestEnvironment { get; }

    public async Task ExecuteAsync(SingleIterationOperationResult result, CancellationToken cancellationToken)
    {
        var neo4jConnectionInfo = await TestEnvironment.GetNeo4jConnectionInfoAsync(cancellationToken);
        var driver = GraphDatabase.Driver(neo4jConnectionInfo.BoltUri, AuthTokens.Basic(neo4jConnectionInfo.UserName, neo4jConnectionInfo.Password));

        using var session = driver.AsyncSession();
        await GetEntityCountAsync(result, session).ConfigureAwait(false);
        await GetEntityTypesCountAsync(result, session).ConfigureAwait(false);
    }

    private async Task GetEntityCountAsync(SingleIterationOperationResult result, IAsyncSession session)
    {
        var query = @$"MATCH (n:Entity)
                            WHERE n.Organization = '{result.Organization.OrganizationId}'
                            RETURN COUNT(*) AS Count";

        var entitiesCount = await session.ExecuteReadAsync(async tx =>
        {
            var result = await tx.RunAsync(query, new Dictionary<string, object>()).ConfigureAwait(false);
            return await result.SingleAsync(record => record.Values["Count"] as long? ?? 0).ConfigureAwait(false);
        }).ConfigureAwait(false);

        result.Output.Add("EntitiesCount", entitiesCount);
    }

    private async Task GetEntityTypesCountAsync(SingleIterationOperationResult result, IAsyncSession session)
    {
        var query = @$"MATCH (n:Entity)
                            WHERE n.Organization = '{result.Organization.OrganizationId}'
                            RETURN DISTINCT labels(n) AS Labels, COUNT(*) AS Count";

        var records = await session.ExecuteReadAsync(async tx =>
        {
            var result = await tx.RunAsync(query, new Dictionary<string, object>()).ConfigureAwait(false);
            return await result.ToListAsync(record =>
            {
                var labels = record.Values["Labels"] as IEnumerable<object>;
                if (labels == null)
                {
                    return null;
                }
                var possibleTypes = labels
                                .Select(label => label as string)
                                .Where(label => label.StartsWith("/"))
                                .OrderBy(label => label);
                if (possibleTypes.Count() == 0)
                {
                    Logger.LogWarning("Unable to find entity type using labels {Labels} because none starts with '/'.", possibleTypes);
                    return null;
                }

                return new
                {
                    Types = possibleTypes,
                    Count = record.Values["Count"] as long? ?? 0
                };
            }).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var entityTypes = records
            .Where(record => record != null)
            .SelectMany(record => record.Types.Select(type => new EntityTypeCount(type, record.Count)))
            .ToList();
        result.Output.Add("EntitiesTypesCount", entityTypes);
    }

    public record EntityTypeCount(string EntityType, long Count);
}
