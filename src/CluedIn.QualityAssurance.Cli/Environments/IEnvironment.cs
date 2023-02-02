using CluedIn.QualityAssurance.Cli.Models.ElasticSearch;
using CluedIn.QualityAssurance.Cli.Models.Neo4j;
using CluedIn.QualityAssurance.Cli.Models.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Models.SqlServer;

namespace CluedIn.QualityAssurance.Cli.Environments;

internal interface IEnvironment
{
    Task SetupAsync(CancellationToken cancellationToken);
    Task TearDownAsync(CancellationToken cancellationToken);
    Task<float> GetAvailableMemoryInMegabytesAsync(CancellationToken cancellationToken);
    Task<RabbitMQConnectionInfo> GetRabbitMqConnectionInfoAsync(CancellationToken cancellationToken);
    Task<Neo4jConnectionInfo> GetNeo4jConnectionInfoAsync(CancellationToken cancellationToken);
    Task<SqlServerConnectionInfo> GetSqlServerConnectionInfoAsync(CancellationToken cancellationToken);
    Task<ElasticSearchConnectionInfo> GetElasticSearchConnectionInfoAsync(CancellationToken cancellationToken);

    Task<ServerUriCollection> GetServerUriCollectionAsync(CancellationToken cancellationToken);
    Task<string> GetNewAccountAccessKeyAsync(CancellationToken cancellationToken);
}
