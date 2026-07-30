using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;

using Bogus;

using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations.Clean;

internal class BenchmarkDatabaseOperation : Operation<BenchmarkDatabaseOptions>
{
    public BenchmarkDatabaseOperation(
        ILogger<BenchmarkDatabaseOperation> logger,
        IEnvironment environment)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }
    private ILogger<BenchmarkDatabaseOperation> Logger { get; }
    private IEnvironment Environment { get; }

    public override async Task ExecuteAsync(BenchmarkDatabaseOptions options, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Begin benchmarking database.");
        var connectionInfo = await Environment.GetSqlServerConnectionInfoAsync(cancellationToken);
        async Task<string> BuildConnectionStringFor(string initialCatalog)
        {
            //var connectionInfo = await Environment.GetSqlServerConnectionInfoAsync(cancellationToken);
            var builder = new SqlConnectionStringBuilder();

            builder.DataSource = $"{connectionInfo.Host},{connectionInfo.Port}";
            builder.UserID = connectionInfo.UserName;
            builder.Password = connectionInfo.Password;
            builder.InitialCatalog = initialCatalog;
            builder.TrustServerCertificate = true;

            return builder.ConnectionString;
        }

        var organizationId = Guid.NewGuid();
        var totalEntities = 100;
        int dataLength = 2_000_000;
        var entityIds = Enumerable.Range(0, totalEntities).Select(x => Guid.NewGuid());

        Logger.LogInformation("Using organization id {OrganizationId}.", organizationId);
        var initialCatalog = "DataStore.Db.BlobStorage";
        var faker = new Faker();
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            //foreach (var entityId in entityIds)
            await Task.WhenAll(entityIds.Select(async (entityId) =>
            {
                await using var connection = new SqlConnection(await BuildConnectionStringFor(initialCatalog));
                await connection.OpenAsync();

                var insertSql = $"""
                    INSERT [dbo].[Blobs] ([Id], [OrganizationId], [EntityId], [Name], [MimeType], [CreationDate], [ModificationDate], [DataLength], [DataMD5], [Data])
                    VALUES (@BlobId, @OrganizationId, @EntityId, @BlobName, @BlobMimeType, NULL, NULL, @DataLength, @DataMD5, @BlobData);
                    """;
                await using var insertCommand = new SqlCommand(insertSql, connection);
                var data = faker.Random.Bytes(dataLength);
                using var md5 = MD5.Create();
                var hash = md5.ComputeHash(data);
                var hashInBase64 = Convert.ToBase64String(hash);

                insertCommand.Parameters.Add(new SqlParameter("@BlobId", Guid.NewGuid()));
                insertCommand.Parameters.Add(new SqlParameter("@OrganizationId", organizationId));
                insertCommand.Parameters.Add(new SqlParameter("@EntityId", entityId));
                insertCommand.Parameters.Add(new SqlParameter("@BlobName", Guid.NewGuid().ToString()));
                insertCommand.Parameters.Add(new SqlParameter("@BlobMimeType", "DummyMimeType"));
                insertCommand.Parameters.Add(new SqlParameter("@DataMD5", hashInBase64));
                insertCommand.Parameters.Add(new SqlParameter("@DataLength", dataLength));
                insertCommand.Parameters.Add(new SqlParameter("@BlobData", data));
                var affected = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            }));
            stopwatch.Stop();
            Logger.LogInformation("Used {TotalSecond} to write.", stopwatch.Elapsed.TotalSeconds);
        }

        await Task.WhenAll(TestUpdate(), TestRead());

        async Task TestUpdate()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            //foreach (var entityId in entityIds)
            await Task.WhenAll(entityIds.Select(async (entityId) =>
            {
                await using var connection = new SqlConnection(await BuildConnectionStringFor(initialCatalog));
                await connection.OpenAsync();

                var updateSql = $"""
                    UPDATE [dbo].[Blobs] 
                    SET
                        [Id] = @BlobId,
                        [OrganizationId] = @OrganizationId,
                        [EntityId] = @EntityId,
                        [Name] = @BlobName,
                        [MimeType] = @BlobMimeType,
                        [CreationDate] = NULL,
                        [ModificationDate] = NULL,
                        [DataLength] = @DataLength,
                        [DataMD5] = @DataMD5,
                        [Data] = @BlobData
                    WHERE [EntityId] = @EntityId;
                    """;
                await using var insertCommand = new SqlCommand(updateSql, connection);
                var data = faker.Random.Bytes(dataLength);
                using var md5 = MD5.Create();
                var hash = md5.ComputeHash(data);
                var hashInBase64 = Convert.ToBase64String(hash);

                insertCommand.Parameters.Add(new SqlParameter("@BlobId", Guid.NewGuid()));
                insertCommand.Parameters.Add(new SqlParameter("@OrganizationId", organizationId));
                insertCommand.Parameters.Add(new SqlParameter("@EntityId", entityId));
                insertCommand.Parameters.Add(new SqlParameter("@BlobName", Guid.NewGuid().ToString()));
                insertCommand.Parameters.Add(new SqlParameter("@BlobMimeType", "DummyMimeType"));
                insertCommand.Parameters.Add(new SqlParameter("@DataMD5", hashInBase64));
                insertCommand.Parameters.Add(new SqlParameter("@DataLength", dataLength));
                insertCommand.Parameters.Add(new SqlParameter("@BlobData", data));
                var affected = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            }));
            stopwatch.Stop();
            Logger.LogInformation("Used {TotalSecond} to update.", stopwatch.Elapsed.TotalSeconds);
        }

        async Task TestRead()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            //foreach (var entityId in entityIds)
            await Task.WhenAll(entityIds.Select(async (entityId) =>
            {
                await using var connection = new SqlConnection(await BuildConnectionStringFor(initialCatalog));
                await connection.OpenAsync();
                var readSql = $"""
                    SELECT [Id], [OrganizationId], [EntityId], [Name], [MimeType], [CreationDate], [ModificationDate], [DataLength], [DataMD5], [Data]
                    FROM [dbo].[Blobs] WHERE [EntityId] = @EntityId;
                    """;

                await using var readCommand = new SqlCommand(readSql, connection);
                readCommand.Parameters.Add(new SqlParameter("@EntityId", entityId));
                var reader = await readCommand.ExecuteReaderAsync(cancellationToken);
                var readBuffer = new byte[dataLength];
                while (await reader.ReadAsync(cancellationToken))
                {
                    var blobData = reader.GetBytes(9, 0, readBuffer, 0, readBuffer.Length);
                }
                await reader.CloseAsync();
            }));
            stopwatch.Stop();
            Logger.LogInformation("Used {TotalSecond} to read.", stopwatch.Elapsed.TotalSeconds);
        }

        Logger.LogInformation("End benchmarking database.");
    }
}
