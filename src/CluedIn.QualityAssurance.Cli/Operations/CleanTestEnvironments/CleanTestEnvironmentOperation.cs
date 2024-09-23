using System.Text;

using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

using Neo4jClient;

using HttpMethod = System.Net.Http.HttpMethod;

namespace CluedIn.QualityAssurance.Cli.Operations.CleanTestEnvironments;

internal class CleanTestEnvironmentOperation : Operation<CleanTestEnvironmentOptions>
{
    private const string OrganizationToKeep = "foobar";
    private const string OrganizationDomainToKeep = OrganizationToKeep + ".com";
    private const string AdminUserToKeep = "admin@" + OrganizationDomainToKeep;

    public CleanTestEnvironmentOperation(
        ILogger<CleanTestEnvironmentOperation> logger,
        IEnvironment environment,
        IHttpClientFactory httpClientFactory,
        IRabbitMQService rabbitMQService)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        RabbitMQService = rabbitMQService ?? throw new ArgumentNullException(nameof(rabbitMQService));
    }

    private ILogger<CleanTestEnvironmentOperation> Logger { get; }
    private IEnvironment Environment { get; }
    private IHttpClientFactory HttpClientFactory { get; }
    private IRabbitMQService RabbitMQService { get; }

    public override async Task ExecuteAsync(CleanTestEnvironmentOptions options, CancellationToken cancellationToken)
    {
        await CleanElasticSearch(cancellationToken);
        await CleanRabbitMq(cancellationToken);
        await CleanSqlServer(cancellationToken);
        await CleanNeo4j(cancellationToken);
    }


    private async Task CleanSqlServer(CancellationToken cancellationToken)
    {
        async Task<string> BuildConnectionStringFor(string initialCatalog)
        {
            var connectionInfo = await Environment.GetSqlServerConnectionInfoAsync(cancellationToken);
            var builder = new SqlConnectionStringBuilder();

            builder.DataSource = $"{connectionInfo.Host},{connectionInfo.Port}";
            builder.UserID = connectionInfo.UserName;
            builder.Password = connectionInfo.Password;
            builder.InitialCatalog = initialCatalog;
            builder.TrustServerCertificate = true;

            return builder.ConnectionString;

        }

        async Task<int> RunScriptOn(string sql, string initialCatalog)
        {
            Logger.LogInformation("Performing cleanup on catalog {CatalogName}.", initialCatalog);
            await using var connection = new SqlConnection(await BuildConnectionStringFor(initialCatalog));
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            Logger.LogDebug("Affected {NumRows}.", affected);
            return affected;
        }

        await RunScriptOn("""
                TRUNCATE TABLE [dbo].[AuditLogChangeSetObjects];
                TRUNCATE TABLE [dbo].AuditLogChangeSet;
                TRUNCATE TABLE [dbo].AuditLogChanges;
                """,
                "DataStore.Db.AuditLog");
        await RunScriptOn($"""
                DELETE FROM [dbo].[AspNetUserRoles]
                WHERE [UserId]=ANY(
                    SELECT ur.[UserId] FROM [dbo].[AspNetUserRoles] ur, [dbo].[AspNetUsers] u
                    WHERE ur.[UserId] = u.[Id]
                    AND u.[UserName] != '{AdminUserToKeep}');

                DELETE FROM [dbo].[AspNetUsers]
                WHERE [Id]=ANY(
                    SELECT u.[Id] FROM [dbo].[AspNetUsers] u, [dbo].[OrganizationAccount] o
                    WHERE u.[OrganizationId] = o.[Id]
                    AND o.[ApplicationSubDomain] != '{OrganizationToKeep}');

                DELETE FROM [dbo].[AspNetRoles]
                WHERE [Id]=ANY(
                    SELECT r.[Id] FROM [dbo].[AspNetRoles] r, [dbo].[OrganizationAccount] o
                    WHERE r.[OrganizationId] = o.[Id]
                    AND o.[ApplicationSubDomain] != '{OrganizationToKeep}');

                DELETE FROM [dbo].[OrganizationDataShard]
                WHERE [OrganizationId]=ANY(
                    SELECT s.[OrganizationId] FROM [dbo].[OrganizationDataShard] s, [dbo].[OrganizationAccount] o
                    WHERE s.[OrganizationId] = o.[Id]
                    AND o.[ApplicationSubDomain] != '{OrganizationToKeep}');
                """,
                "DataStore.Db.Authentication");
        await RunScriptOn("""
                TRUNCATE TABLE [dbo].[AuditLogChangeSetObjects];
                TRUNCATE TABLE [dbo].AuditLogChangeSet;
                TRUNCATE TABLE [dbo].AuditLogChanges;
                """,
                "DataStore.Db.AuditLog");

        async Task<Guid> GetOrganizationId()
        {
            await using var connection = new SqlConnection(await BuildConnectionStringFor("DataStore.Db.OpenCommunication"));
            await connection.OpenAsync();
            var sql = $"SELECT [Id] FROM [dbo].[OrganizationProfile] op WHERE op.[OrganizationName] = '{OrganizationToKeep}'";
            await using var command = new SqlCommand(sql, connection);
            var id = await command.ExecuteScalarAsync(cancellationToken);

            return id as Guid? ?? throw new ApplicationException("Failed to find foobar organization");
        }

        var organizationId = await GetOrganizationId();
        int blobDeletionCap = 10000;
        while(await RunScriptOn($"""
                DECLARE @organizationid uniqueidentifier;
                SET @organizationId = '{organizationId}';
                DELETE TOP ({blobDeletionCap}) FROM [dbo].[Blobs] WHERE [OrganizationId] != @organizationId;
                """,
                "DataStore.Db.BlobStorage") == blobDeletionCap)
        {
            Logger.LogDebug("There are still rows to delete, continuing...");
        }
        await RunScriptOn($"""
                DECLARE @organizationid uniqueidentifier;
                SET @organizationId = '{organizationId}';
                DELETE FROM [dbo].[Configuration] WHERE [OrganizationId] != @organizationId;
                """,
                "DataStore.Db.Configuration");
        await RunScriptOn($"""
                TRUNCATE TABLE [dbo].[Logs_ClearBit];
                TRUNCATE TABLE [dbo].[Logs_GoogleKnowledgeGraph];
                TRUNCATE TABLE [dbo].[Logs_Web];
                TRUNCATE TABLE [dbo].[ExternalSearchQuery];
                DELETE FROM [dbo].[ExternalSearchImage];
                DELETE FROM [dbo].[ExternalSearchBlob];
                """,
                "DataStore.Db.ExternalSearch");
        await RunScriptOn($"""
                TRUNCATE TABLE [dbo].[EntityMetricValueIntHistory];
                TRUNCATE TABLE [dbo].[MetricValueIntHistory];
                TRUNCATE TABLE [dbo].[MetricValuePctHistory];
                TRUNCATE TABLE [dbo].[EntityMetricValueInt];
                TRUNCATE TABLE [dbo].[EntityMetricValuePct];
                DELETE FROM [dbo].[DateDimension];
                DELETE FROM [dbo].[Dimension];
                DELETE FROM [dbo].[Metric];
                """,
                "DataStore.Db.Metrics");
        await RunScriptOn($"""
                TRUNCATE TABLE [dbo].[annotationEdgeProperty];
                TRUNCATE TABLE [dbo].[annotationPreProcessRules];
                TRUNCATE TABLE [dbo].[annotationProperties];
                TRUNCATE TABLE [dbo].[annotationPropertyCodes];
                TRUNCATE TABLE [dbo].[annotationEdge];
                TRUNCATE TABLE [dbo].[AnnotationReceipts];
                DELETE FROM [dbo].[DataSetAnnotationMappings];
                DELETE FROM [dbo].[DataSetEndpointIdToPushes];
                DELETE FROM [dbo].[DataSetEndpointReceipts];
                DELETE FROM [dbo].[annotations];
                DELETE FROM [dbo].[DataSets];
                DELETE FROM [dbo].[DataSources];
                DELETE FROM [dbo].[DataSourceSets];
                DELETE FROM [dbo].[submissions];
                WHILE EXISTS(
                    SELECT 
                        [NAME] AS TO_DROP
                    FROM 
                        sys.tables
                    WHERE [Name] LIKE 'dataSetOperationEndpointIdToPush_%')
                BEGIN
                    DECLARE @tblname SYSNAME;
                    SET @tblname = (SELECT TOP 1
                        [NAME] AS TO_DROP
                    FROM 
                        sys.tables
                    WHERE [Name] LIKE 'dataSetOperationEndpointIdToPush_%');
                    DECLARE @dropDDL NVARCHAR(1000) = 'DROP TABLE ' + QUOTENAME(@tblname);

                    IF EXISTS (SELECT 1 FROM sys.objects WHERE name = @tblname)
                    EXEC(@dropDDL)
                END
                WHILE EXISTS(
                    SELECT 
                        [NAME] AS TO_DROP
                    FROM 
                        sys.tables
                    WHERE [Name] LIKE 'DataSetLogs_%')
                BEGIN
                    DECLARE @tblname1 SYSNAME;
                    SET @tblname1 = (SELECT TOP 1
                        [NAME] AS TO_DROP
                    FROM 
                        sys.tables
                    WHERE [Name] LIKE 'DataSetLogs_%');
                    DECLARE @dropDDL1 NVARCHAR(1000) = 'DROP TABLE ' + QUOTENAME(@tblname1);

                    IF EXISTS (SELECT 1 FROM sys.objects WHERE name = @tblname1)
                    EXEC(@dropDDL1)
                END
                """,
                "DataStore.Db.Microservices");
        await RunScriptOn($"""
                DECLARE @organizationid uniqueidentifier;
                SET @organizationId = '{organizationId}';
                DELETE FROM [dbo].[PageTemplate] WHERE [OrganizationId] != @organizationId;
                TRUNCATE TABLE [dbo].[VocabularyDefinition];
                TRUNCATE TABLE [dbo].[VocabularyKeyDefinition];
                TRUNCATE TABLE [dbo].[VocabularyKeyGroupDefinition];
                TRUNCATE TABLE [dbo].[VocabularyOwner];
                DELETE FROM [dbo].[EntityType] WHERE [Type] LIKE '/testX%'
                DELETE FROM [dbo].[PageTemplate] WHERE [OrganizationId] != @organizationId;
                DELETE FROM [dbo].[OrganizationProviderAccounts] WHERE [OrganizationId] != @organizationId;
                DELETE FROM [dbo].[OrganizationProvider] WHERE [OrganizationId] != @organizationId;
                DELETE FROM [dbo].[UserProfile] WHERE [OrganizationId] != @organizationId;
                DELETE FROM [dbo].[OrganizationProfile] WHERE [Id] != @organizationId;
                """,
                "DataStore.Db.OpenCommunication");
    }

    private async Task CleanRabbitMq(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Begin cleaning rabbitmq.");
        HashSet<string> queuePrefixesOfInterest = ["clue_datasource_", "dataset_failed_"];
        var queuesInfo = await RabbitMQService.GetRabbitAllQueueInfoAsync(cancellationToken);

        var queuesOfInterest = queuesInfo.Where(queue => queuePrefixesOfInterest.Any(prefix => queue.QueueName.StartsWith(prefix)));
        var exceptions = new List<Exception>();
        foreach (var queue in queuesOfInterest)
        {
            try
            {
                await RabbitMQService.DeleteQueueAsync(queue.QueueName, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to delete queue {QueueName}", queue.QueueName);
                exceptions.Add(ex);
            }
        }

        if (exceptions.Any())
        {
            throw new AggregateException(exceptions);
        }

        Logger.LogInformation("End cleaning rabbitmq.");
    }

    private async Task CleanElasticSearch(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Begin cleaning elastic search.");
        var connectionInfo = await Environment.GetElasticSearchConnectionInfoAsync(cancellationToken);

        var getIndicesUrl = new Uri(connectionInfo.ServerUri, "_cat/indices?format=json");
        var request = new HttpRequestMessage(HttpMethod.Get, getIndicesUrl);

        var authenticationString = $"{connectionInfo.UserName}:{connectionInfo.Password}";
        var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.UTF8.GetBytes(authenticationString));
        request.Headers.Add("Authorization", "Basic " + base64EncodedAuthenticationString);

        var client = HttpClientFactory.CreateClient(Constants.AllowUntrustedSSLClient);
        var response = await client.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var results = await response.Content.DeserializeToAnonymousTypeAsync(new []
        {
            new
            {
                index = (string?) null,
            },
        }) ?? [];

        var indexNames = results
            .Select(entry => entry.index?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => name != null && name != OrganizationToKeep)
            .ToList();

        var exceptions = new List<Exception>();
        foreach (var indexName in indexNames)
        {
            if (indexName == null)
            {
                continue;
            }

            if (indexName.Equals(OrganizationToKeep, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                Logger.LogInformation("Deleting index {IndexName}.", indexName);
                var deleteUrl = new Uri(connectionInfo.ServerUri, indexName);
                var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, deleteUrl);
                deleteRequest.Headers.Add("Authorization", "Basic " + base64EncodedAuthenticationString);
                var deleteResponse = await client.SendAsync(deleteRequest).ConfigureAwait(false);
                deleteResponse.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to delete index {IndexName}", indexName);
                exceptions.Add(ex);
            }
        }

        if (exceptions.Any())
        {
            throw new AggregateException(exceptions);
        }
        Logger.LogInformation("End cleaning elastic search.");
    }

    private async Task CleanNeo4j(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Begin cleaning neo4j.");
        var connectionInfo = await Environment.GetNeo4jConnectionInfoAsync(cancellationToken);
        var client = new BoltGraphClient(connectionInfo.BoltUri, connectionInfo.UserName, connectionInfo.Password);
        await client.ConnectAsync();
        client.DefaultDatabase = connectionInfo.DatabaseName;
        var query = client.Cypher.Call("db.labels()").Yield("label").Return<string>("label");
        var result = await query.ResultsAsync;

        var labels = result.Where(x => x.StartsWith(OrganizationToKeep) && x != OrganizationToKeep).ToList();
        var neo4jDeletionLimit = 50000;
        foreach (var currentLabel in labels)
        {
            Logger.LogInformation("Start deleting for {Label}", currentLabel);
            while (true)
            {
                var deleteQuery = client.Cypher
                    .Match($"(n:`{currentLabel}`)")
                    .With($"n LIMIT {neo4jDeletionLimit}")
                    .DetachDelete("n")
                    .Return<int>("COUNT (n) AS deletedCount");
                var resultDelete = await deleteQuery.ResultsAsync;
                var deleted = resultDelete.SingleOrDefault();
                Logger.LogInformation("Deleted: {deleted}", deleted);
                if (deleted == 0)
                {
                    Logger.LogInformation("Finished deleting for {Label}", currentLabel);
                    break;
                }
            }
        }
        Logger.LogInformation("End cleaning neo4j.");
    }
}
