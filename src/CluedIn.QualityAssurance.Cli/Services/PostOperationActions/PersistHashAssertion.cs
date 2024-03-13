using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using CluedIn.QualityAssurance.Cli.Operations.ClueSending;
using CsvHelper;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using Nest;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CluedIn.QualityAssurance.Cli.Services.PostOperationActions;

internal class PersistHashAssertion : IPostOperationAction
{
    public PersistHashAssertion(ILogger<PersistHashAssertion> logger, IEnvironment testEnvironment, IHttpClientFactory httpClientFactory, IClueSendingOperationOptions options)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TestEnvironment = testEnvironment ?? throw new ArgumentNullException(nameof(testEnvironment));
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private ILogger<PersistHashAssertion> Logger { get; }
    private IEnvironment TestEnvironment { get; }
    public IHttpClientFactory HttpClientFactory { get; }
    public IClueSendingOperationOptions Options { get; }

    public async Task ExecuteAsync(SingleIterationOperationResult result, CancellationToken cancellationToken)
    {
        var neo4jConnectionInfo = await TestEnvironment.GetNeo4jConnectionInfoAsync(cancellationToken);
        var elasticConnectionInfo = await TestEnvironment.GetElasticSearchConnectionInfoAsync(cancellationToken);

        var elasticSettings = new ConnectionSettings(elasticConnectionInfo.ServerUri)
            .DefaultIndex(result.Organization.ClientId)
            .BasicAuthentication(elasticConnectionInfo.UserName, elasticConnectionInfo.Password);
        var elasticClient = new ElasticClient(elasticSettings);


        // TODO: Don't load all entities in memory. Just do one page at a time and compare using string comparison instead of dictionary
        var sqlServerEntities = await GetFromCluedInAsync(result, cancellationToken).ConfigureAwait(false);
        var elasticEntities = await GetFromElasticAsync(elasticClient).ConfigureAwait(false);
        var neoEntities = await GetFromNeo4jAsync(result, cancellationToken).ConfigureAwait(false);

        int elasticMissingCount = 0;
        int neoMissingCount = 0;
        int elasticDifferentHashCount = 0;
        int neoDifferentHashCount = 0;

        var failedEntities = new List<FailedEntity>();
        foreach (var currentSqlEntity in sqlServerEntities)
        {
            bool failed = false;
            if (!elasticEntities.TryGetValue(currentSqlEntity.Key, out var elasticValue))
            {
                elasticMissingCount++;
                failed= true;
            }
            else if (elasticValue != currentSqlEntity.Value)
            {
                elasticDifferentHashCount++;
                failed = true;
            }

            if (!neoEntities.TryGetValue(currentSqlEntity.Key, out var neoValue))
            {
                neoMissingCount++;
                failed = true;
            }
            else if (neoValue != currentSqlEntity.Value)
            {
                neoDifferentHashCount++;
                failed = true;
            }

            if (failed)
            {
                failedEntities.Add(new FailedEntity
                {
                    EntityId = currentSqlEntity.Key,
                    SqlServerHash= currentSqlEntity.Value,
                    Neo4jHash = neoValue,
                    ElasticSearchHash = elasticValue
                });
            }
        }

        result.Output.Add("MissingEntities.Elastic", elasticMissingCount);
        result.Output.Add("DifferentHash.Elastic", elasticDifferentHashCount);
        result.Output.Add("MissingEntities.Neo4j", neoMissingCount);
        result.Output.Add("DifferentHash.Neo4j", neoDifferentHashCount);


        using var writer = new StreamWriter(Path.Combine(Options.OutputDirectory, $"persisthash-{result.Organization.ClientId}.csv"));
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(failedEntities);
    }

    private async Task<IDictionary<string, string>> GetFromCluedInAsync(SingleIterationOperationResult result, CancellationToken cancellationToken)
    {
        var allEntities = new List<Entity>();
        var sqlConnectionInfo = await TestEnvironment.GetSqlServerConnectionInfoAsync(cancellationToken);
        var uris = await TestEnvironment.GetServerUriCollectionAsync(cancellationToken);
        var httpClient = HttpClientFactory.CreateClient(Constants.AllowUntrustedSSLClient);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Organization.AccessToken);
        var connectionString = sqlConnectionInfo.CreateConnectionString();
        var query = @$"SELECT CONVERT(NVARCHAR(36), [EntityId]) as Id
                        FROM [DataStore.Db.BlobStorage].[dbo].[Blobs]
                        WHERE [OrganizationId] = @OrganizationId
                        ORDER BY Id
                        OFFSET @start ROWS
                        FETCH NEXT @pageSize ROWS ONLY";
        var start = 0;
        var pageSize = Constants.PagingPageSize;
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            while (true)
            {
                var parameters = new { result.Organization.OrganizationId, start, pageSize };
                var entities = await connection.QueryAsync<Entity>(query, parameters).ConfigureAwait(false);
                foreach (var entity in entities)
                {
                    var response = await httpClient.GetFromJsonAsync<EntityResponse>(new Uri(uris.WebApiUri, $"api/entity?id={entity.Id}&full=true"), cancellationToken: cancellationToken).ConfigureAwait(false);

                    allEntities.Add(new Entity(entity.Id, response?.Entity?.PersistHash ?? string.Empty));
                }
                if (entities.Count() < pageSize)
                {
                    break;
                }
                start += pageSize;
            }
        }

        return allEntities.ToDictionary(entity => entity.Id, entity => entity.PersistHash);
    }

    private async Task<IDictionary<string, string>> GetFromElasticAsync(ElasticClient elasticClient)
    {
        var allEntities = new List<Entity>();

        var pageSize = Constants.PagingPageSize;
        var start = 0;
        while (true)
        {
            var results = await elasticClient.SearchAsync<Entity>(
                query => query.MatchAll()
                .Sort(sortDescriptor => sortDescriptor.Ascending(entity => entity.Id))
                .Size(pageSize)
                .Skip(start)).ConfigureAwait(false);
            allEntities.AddRange(results.Documents);
            start += pageSize;
            if (start >= results.Total || results.Documents.Count < pageSize)
            {
                break;
            }
        }

        return allEntities.ToDictionary(entity => entity.Id, entity => entity.PersistHash);
    }

    private async Task<IDictionary<string, string>> GetFromNeo4jAsync(SingleIterationOperationResult result, CancellationToken cancellationToken)
    {
        var neo4jConnectionInfo = await TestEnvironment.GetNeo4jConnectionInfoAsync(cancellationToken);
        var driver = GraphDatabase.Driver(neo4jConnectionInfo.BoltUri, AuthTokens.Basic(neo4jConnectionInfo.UserName, neo4jConnectionInfo.Password));

        using var session = driver.AsyncSession();
        var allEntities = new List<Entity>();

        var pageSize = Constants.PagingPageSize;
        var start = 0;
        while (true)
        {
            var query = @$"MATCH (n:Entity)
                        WHERE n.Organization ='{result.Organization.OrganizationId}'
                        RETURN n.Id as Id, n.PersistHash AS PersistHash
                        ORDER BY Id
                        SKIP {start} LIMIT {pageSize}";


            var results = await session.ExecuteReadAsync(async tx =>
            {
                var result = await tx.RunAsync(query, new Dictionary<string, object>()).ConfigureAwait(false);
                return await result.ToListAsync(record =>
                {
                    return new Entity(
                        record["Id"].As<string>(),
                        record["PersistHash"].As<string>()
                    );
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);


            allEntities.AddRange(results);
            start += pageSize;
            if (results.Count < pageSize)
            {
                break;
            }
        }

        return allEntities.ToDictionary(entity => entity.Id, entity => entity.PersistHash);
    }

    public record Entity(string Id, string PersistHash);

    public class EntityResponse
    {
        public EntityResponseEntity Entity { get; set; }
    }

    public class EntityResponseEntity
    {
        [JsonPropertyName("attribute-id")]
        public string Id { get; set; }

        [JsonPropertyName("attribute-persistHash")]
        public string PersistHash { get; set; }
    }

    public class FailedEntity
    {
        public string EntityId { get; set; }

        public string? SqlServerHash { get; set; }
        public string? Neo4jHash { get; set; }
        public string? ElasticSearchHash { get; set; }
    }
}
