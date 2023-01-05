using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Models.Operations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Nest;
using Dapper;

namespace CluedIn.QualityAssurance.Cli.Services.PostOperationActions;

internal class MetricsAssertionAction : IPostOperationAction
{
    public MetricsAssertionAction(ILogger<CodesAssertionAction> logger, IEnvironment testEnvironment)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TestEnvironment = testEnvironment ?? throw new ArgumentNullException(nameof(testEnvironment));
    }

    private ILogger<CodesAssertionAction> Logger { get; }
    private IEnvironment TestEnvironment { get; }

    public async Task ExecuteAsync(SingleIterationOperationResult result, CancellationToken cancellationToken)
    {
        var elasticConnectionInfo = await TestEnvironment.GetElasticSearchConnectionInfoAsync(cancellationToken);
        var elasticSettings = new ConnectionSettings(elasticConnectionInfo.ServerUri)
            .DefaultIndex(result.Organization.ClientId)
            .BasicAuthentication(elasticConnectionInfo.UserName, elasticConnectionInfo.Password);
        var elasticClient = new ElasticClient(elasticSettings);
        var entities = await GetNonShadowEntitiesFromElasticAsync(result, elasticClient).ConfigureAwait(false);

        await CheckEntitiesWithNoMetrics(result, entities, cancellationToken).ConfigureAwait(false);
        await CheckBillableRecordMetricCount(result, entities, cancellationToken).ConfigureAwait(false);
    }

    private async Task CheckBillableRecordMetricCount(SingleIterationOperationResult result, ICollection<string> entities, CancellationToken cancellationToken)
    {
        var sqlConnectionInfo = await TestEnvironment.GetSqlServerConnectionInfoAsync(cancellationToken);
        var connectionString = sqlConnectionInfo.CreateConnectionString();

        // TODO: Check page by page to reduce memory consumption
        var allMetrics = new List<Metric>();
        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();
            var query = @"SELECT COUNT([EntityId])
  FROM [DataStore.Db.Metrics].[dbo].[EntityMetricValueInt] AS Value, [DataStore.Db.Metrics].[dbo].Dimension AS Dimension
  WHERE Value.DimensionId = Dimension.Id
  AND Dimension.AccountId = @OrganizationId
  AND Dimension.MetricId = 'A5EEA4E9-D1BE-4923-BB62-F2FFF1291CEF'";
            var parameters = new { result.Organization.OrganizationId };
            var count = await connection.ExecuteScalarAsync<long>(query, parameters).ConfigureAwait(false);
            result.Output.Add("BillableMetricsCount", count);
        }
    }
    private async Task CheckEntitiesWithNoMetrics(SingleIterationOperationResult result, ICollection<string> entities, CancellationToken cancellationToken)
    {
        var sqlConnectionInfo = await TestEnvironment.GetSqlServerConnectionInfoAsync(cancellationToken);
        var connectionString = sqlConnectionInfo.CreateConnectionString();

        // TODO: Check page by page to reduce memory consumption
        var allMetrics = new List<Metric>();
        using (var connection = new SqlConnection(connectionString))
        {
            int pageSize = Constants.PagingPageSize;
            connection.Open();
            {
                int start = 0;
                var query = @"SELECT DISTINCT [EntityId]
  FROM [DataStore.Db.Metrics].[dbo].[EntityMetricValueInt] AS Value, [DataStore.Db.Metrics].[dbo].Dimension AS Dimension
  WHERE Value.DimensionId = Dimension.Id
  AND Dimension.AccountId = @OrganizationId
  ORDER BY [EntityId]
  OFFSET @start ROWS
  FETCH NEXT @pageSize ROWS ONLY";
                while (true)
                {
                    var parameters = new { result.Organization.OrganizationId, start, pageSize };
                    var metrics = await connection.QueryAsync<Metric>(query, parameters).ConfigureAwait(false);
                    allMetrics.AddRange(metrics);
                    if (metrics.Count() < pageSize)
                    {
                        break;
                    }
                    start += pageSize;
                }
            }
            {
                int start = 0;
                var query = @"SELECT DISTINCT [EntityId]
  FROM [DataStore.Db.Metrics].[dbo].[EntityMetricValueInt] AS Value, [DataStore.Db.Metrics].[dbo].Dimension AS Dimension
  WHERE Value.DimensionId = Dimension.Id
  AND Dimension.AccountId = @OrganizationId
  ORDER BY [EntityId]
  OFFSET @start ROWS
  FETCH NEXT @pageSize ROWS ONLY";
                while (true)
                {
                    var parameters = new { result.Organization.OrganizationId, start, pageSize };
                    var metrics = await connection.QueryAsync<Metric>(query, parameters).ConfigureAwait(false);
                    allMetrics.AddRange(metrics);
                    if (metrics.Count() < pageSize)
                    {
                        break;
                    }

                    start += pageSize;
                }
            }
        }

        var entityWithMetrics = allMetrics.Select(metric => metric.EntityId.ToString()).ToHashSet();

        var entityWithNoMetrics = entities.Where(entity => !entityWithMetrics.Contains(entity));
        result.Output.Add("EntitiesWithNoMetricsCount", entityWithNoMetrics.Count());
    }

    private async Task<ICollection<string>> GetNonShadowEntitiesFromElasticAsync(SingleIterationOperationResult result, ElasticClient elasticClient)
    {
        var allEntities = new List<string>();

        var pageSize = Constants.PagingPageSize;
        var start = 0;
        while (true)
        {
            var results = await elasticClient
                .SearchAsync<ElasticEntity>(
                document => document.Query(
                query => query.Bool(
                    boolean => boolean.Must(
                        condition1 => condition1.Match(query => query.Field(entity => entity.OrganizationId).Query(result.Organization.OrganizationId.ToString().ToLowerInvariant())),
                        condition2 => condition2.Match(query => query.Field(entity => entity.IsShadowEntity).Query("false"))
                    ).MustNot(
                        condition1 => condition1.Prefix(query => query.Field(entity => entity.Codes).Value("/Infrastructure/User#CluedIn")),
                        condition2 => condition2.Prefix(query => query.Field(entity => entity.Codes).Value("/Organization#CluedIn"))
                    )
                )
            )
            .Sort(sortDescriptor => sortDescriptor.Ascending(entity => entity.Id))
            .Size(pageSize)
            .Skip(start)).ConfigureAwait(false);
            allEntities.AddRange(results.Documents.Select(d => d.Id));
            start += pageSize;
            if (start >= results.Total || results.Documents.Count < pageSize)
            {
                break;
            }
        }


        return allEntities;
    }

    public class Metric
    {
        public Guid EntityId { get; set; }

        public int Value { get; set; }
    }

    public class ElasticEntity
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public bool IsShadowEntity { get; set; }
        public ICollection<string> Codes { get; set; }
    }
}
