using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Models.ElasticSearch;
using CluedIn.QualityAssurance.Cli.Models.Neo4j;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using CluedIn.QualityAssurance.Cli.Models.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Neo4jClient;
using System.Net.Http.Json;
using System.Text;

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
        var neo4jConnectionInfo = await TestEnvironment.GetNeo4jConnectionInfoAsync(cancellationToken).ConfigureAwait(false);
        var elasticSearchConnectionInfo = await TestEnvironment.GetElasticSearchConnectionInfoAsync(cancellationToken).ConfigureAwait(false);
        var sqlServerConnectionInfo = await TestEnvironment.GetSqlServerConnectionInfoAsync(cancellationToken).ConfigureAwait(false);

        var organizationId = result.Organization.OrganizationId;
        var entityTypes = await GetEntityTypesAsync(organizationId, neo4jConnectionInfo).ConfigureAwait(false);

        var neo4jResultTask = GetResultForNeo(organizationId, entityTypes, neo4jConnectionInfo);
        var elasticSearchResultTask = GetResultForElasticSearch(organizationId, entityTypes, elasticSearchConnectionInfo);
        var sqlServerResultTask = GetResultForSql(organizationId, entityTypes, sqlServerConnectionInfo);

        await Task.WhenAll(neo4jResultTask, elasticSearchResultTask, sqlServerResultTask).ConfigureAwait(false);

        var neoResult = await neo4jResultTask;
        var esResult = await elasticSearchResultTask;
        var sqlResult = await sqlServerResultTask;

        result.Output.Add("EntitiesTypesCount.Neo4j", neoResult);
        result.Output.Add("EntitiesTypesCount.ElasticSearch", esResult);
        result.Output.Add("EntitiesTypesCount.SqlServer", sqlResult);
    }

    private async Task<ICollection<string>> GetEntityTypesAsync(Guid organizationId, Neo4jConnectionInfo connectionInfo)
    {
        var entityTypes = new List<string>();
        var client = new BoltGraphClient(connectionInfo.BoltUri, connectionInfo.UserName, connectionInfo.Password);
        await client.ConnectAsync();
        client.DefaultDatabase = connectionInfo.DatabaseName;
        var labelsQuery = client.Cypher.Call("db.labels()").Yield("label");
        var labelsResultQuery = labelsQuery.Return<string?>("label");

        var labels = (await labelsResultQuery.ResultsAsync).ToList();
        var entityTypeLabelsOfInterest = labels
            .Where(currentLabel => currentLabel != null && currentLabel.StartsWith("/") && !currentLabel.StartsWith("/Temporal"));

        foreach (var currentLabel in entityTypeLabelsOfInterest)
        {
            var match = $"(n:`{currentLabel}`:Entity)";
            var firstEntityQuery = client.Cypher.Match(match)
                .Where($"n.`Organization` = '{organizationId}'");
            var firstEntityResultQuery = firstEntityQuery.Return<int?>("1").Limit(1);
            var firstEntityResult = (await firstEntityResultQuery.ResultsAsync).SingleOrDefault();

            if (firstEntityResult != null)
            {
                entityTypes.Add(currentLabel!);
            }
        }

        return entityTypes;
    }

    private async Task<Dictionary<string, EntityTypeCount>> GetResultForSql(Guid organizationId, ICollection<string> entityTypes, SqlServerConnectionInfo connectionInfo)
    {
        var result = new Dictionary<string, EntityTypeCount>();
        var builder = new SqlConnectionStringBuilder();

        builder.DataSource = $"{connectionInfo.Host},{connectionInfo.Port}";
        builder.UserID = connectionInfo.UserName;
        builder.Password = connectionInfo.Password;
        builder.InitialCatalog = "DataStore.Db.BlobStorage";
        builder.TrustServerCertificate = true;

        using (var connection = new SqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            foreach (var currentEntityType in entityTypes)
            {
                var sql = "SELECT COUNT(*) FROM Blobs WHERE OrganizationId = @OrganizationId AND [Name] LIKE @Name";


                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.Add(new SqlParameter()
                    {
                        ParameterName = "OrganizationId",
                        Value = organizationId,
                    });
                    command.Parameters.Add(new SqlParameter()
                    {
                        ParameterName = "Name",
                        Value = $"{currentEntityType}#%",
                    });
                    var count = await command.ExecuteScalarAsync() as int?;
                    if (count == null)
                    {
                        throw new InvalidOperationException("Error trying to execute query to get entity count.");
                    }
                    result.Add(currentEntityType, new(count.Value, count.Value));
                }
            }
        }
        return result;
    }

    private async Task<Dictionary<string, EntityTypeCount>> GetResultForNeo(Guid organizationId, ICollection<string> entityTypes, Neo4jConnectionInfo connectionInfo)
    {
        var result = new Dictionary<string, EntityTypeCount>();
        var client = new BoltGraphClient(connectionInfo.BoltUri, connectionInfo.UserName, connectionInfo.Password);
        await client.ConnectAsync();
        client.DefaultDatabase = connectionInfo.DatabaseName;

        foreach (var currentEntityType in entityTypes)
        {
            var nonShadowQuery = client.Cypher.Match($"(n:`{currentEntityType}` {{Organization: '{organizationId}', IsShadowEntity: 'False'}})");
            var nonShadowResultQuery = nonShadowQuery.Return<long?>("count(n)");
            var nonShadow = (await nonShadowResultQuery.ResultsAsync).SingleOrDefault();
            var totalQuery = client.Cypher.Match($"(n:`{currentEntityType}` {{Organization: '{organizationId}'}})");
            var totalResultQuery = totalQuery.Return<long?>("count(n)");
            var total = (await totalResultQuery.ResultsAsync).SingleOrDefault();

            result.Add(currentEntityType, new(nonShadow.GetValueOrDefault(-1), total.GetValueOrDefault(-1)));
        }
        return result;
    }

    private async Task<Dictionary<string, EntityTypeCount>> GetResultForElasticSearch(Guid organizationId, ICollection<string> entityTypes, ElasticSearchConnectionInfo connectionInfo)
    {
        var result = new Dictionary<string, EntityTypeCount>();
        foreach (var currentEntityType in entityTypes)
        {
            var client = new HttpClient();
            var nonShadowRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(connectionInfo.ServerUri, "_count"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connectionInfo.UserName}:{connectionInfo.Password}"));
            nonShadowRequest.Headers.Add("Authorization", $"Basic {credentials}");

            var nonShadowBody = $@"
            {{
              ""query"" : {{
                ""bool"": {{
                    ""must"": [
                        {{
                            ""term"" : {{ ""organizationId"" : ""{organizationId}"" }}
                        }},
                        {{
                            ""term"" : {{ ""entityType"" : ""{currentEntityType}"" }}
                        }},
                        {{
                            ""term"" : {{ ""isShadowEntity"" : false }}
                        }}
                    ]
                }} 
              }}
            }}";
            nonShadowRequest.Content = new StringContent(nonShadowBody, null, "application/json");
            var nonShadowResponse = await client.SendAsync(nonShadowRequest);
            nonShadowResponse.EnsureSuccessStatusCode();
            var nonShadowResult = await nonShadowResponse.Content.ReadFromJsonAsync<Result>();

            var withShadowRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(connectionInfo.ServerUri, "_count"));
            withShadowRequest.Headers.Add("Authorization", $"Basic {credentials}");

            var withShadowBody = $@"
            {{
              ""query"" : {{
                ""bool"": {{
                    ""must"": [
                        {{
                            ""term"" : {{ ""organizationId"" : ""{organizationId}"" }}
                        }},
                        {{
                            ""term"" : {{ ""entityType"" : ""{currentEntityType}"" }}
                        }}
                    ]
                }} 
              }}
            }}";
            withShadowRequest.Content = new StringContent(withShadowBody, null, "application/json");
            var withShadowResponse = await client.SendAsync(withShadowRequest);
            withShadowResponse.EnsureSuccessStatusCode();
            var withShadowResult = await withShadowResponse.Content.ReadFromJsonAsync<Result>();

            result.Add(currentEntityType, new(nonShadowResult?.count ?? -1, withShadowResult?.count ?? -1));
        }
        return result;
    }

    private record Result(long count, Shard _shards);
    private record Shard(int total, int successful, int skipped, int failed);
    private record EntityTypeCount(long NonShadowCount, long TotalCount);
}
