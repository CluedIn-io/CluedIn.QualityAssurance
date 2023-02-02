using System.Diagnostics;
using System.Runtime.InteropServices;
using CluedIn.QualityAssurance.Cli.Models.ElasticSearch;
using CluedIn.QualityAssurance.Cli.Models.Neo4j;
using CluedIn.QualityAssurance.Cli.Models.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Models.SqlServer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CluedIn.QualityAssurance.Cli.Environments;

internal class LocalEnvironment : IEnvironment
{
    public LocalEnvironment(ILogger<LocalEnvironment> logger, IOptions<ILocalEnvironmentOptions> options)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private ILogger<LocalEnvironment> Logger { get; }
    private IOptions<ILocalEnvironmentOptions> Options { get; }

    public Task SetupAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Nothing to do for setting up of this environment.");
        return Task.CompletedTask;
    }

    public Task TearDownAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Nothing to do for tear down of this environment.");
        return Task.CompletedTask;
    }

    private static bool IsUnix()
    {
        var isUnix = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                     RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        return isUnix;
    }

    public Task<float> GetAvailableMemoryInMegabytesAsync(CancellationToken cancellationToken)
    {
        if (IsUnix())
        {
            Logger.LogWarning("Unable to get available memory in Unix platform. Returning -1.");
            return Task.FromResult(-1.0f);
        }

        Logger.LogInformation("Start getting available memory.");
        var performance = new PerformanceCounter("Memory", "Available MBytes");
        var availableMemory = performance.NextValue();
        Logger.LogInformation("End getting available memory is {AvailableMemoryInMegabytes}", availableMemory);
        return Task.FromResult(availableMemory);
    }

    public Task<RabbitMQConnectionInfo> GetRabbitMqConnectionInfoAsync(CancellationToken cancellationToken)
    {
        var result = new RabbitMQConnectionInfo
        {
            ManagementUri = new Uri(Options.Value.RabbitMQManagementUri),
            Password = Options.Value.RabbitUserPassword,
            UserName = Options.Value.RabbitUserName,
        };
        return Task.FromResult(result);
    }

    public Task<Neo4jConnectionInfo> GetNeo4jConnectionInfoAsync(CancellationToken cancellationToken)
    {
        var result = new Neo4jConnectionInfo
        {
            BoltUri = new Uri(Options.Value.Neo4jBoltUri),
            Password = Options.Value.Neo4jUserName,
            UserName = Options.Value.Neo4jUserPassword,
        };
        return Task.FromResult(result);
    }

    public Task<ServerUriCollection> GetServerUriCollectionAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new ServerUriCollection(
            AuthApiUri: EnsureTrailingSlash(new Uri(Options.Value.AuthApiUrl)),
            PublicApiUri: EnsureTrailingSlash(new Uri(Options.Value.PublicApiUrl)),
            WebApiUri: EnsureTrailingSlash(new Uri(Options.Value.WebApiUrl)),
            UiGraphqlUri: EnsureTrailingSlash(new Uri(Options.Value.UiGraphqlUrl)),
            UploadApiUri: EnsureTrailingSlash(new Uri(Options.Value.UploadApiUrl))
            ));
    }

    public Task<string> GetNewAccountAccessKeyAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Options.Value.NewAccountAccessKey);
    }

    private Uri EnsureTrailingSlash(Uri uri)
    {
        if (!uri.AbsoluteUri.EndsWith("/"))
        {
            return new Uri(uri.AbsoluteUri + "/");
        }

        return uri;
    }

    public Task<SqlServerConnectionInfo> GetSqlServerConnectionInfoAsync(CancellationToken cancellationToken)
    {
        var result = new SqlServerConnectionInfo
        {
            Host = Options.Value.SqlServerHost,
            Port = Options.Value.SqlServerPort,
            UserName = Options.Value.SqlServerUserName,
            Password = Options.Value.SqlServerUserPassword,
        };
        return Task.FromResult(result);
    }

    public Task<ElasticSearchConnectionInfo> GetElasticSearchConnectionInfoAsync(CancellationToken cancellationToken)
    {
        var result = new ElasticSearchConnectionInfo
        {
            ServerUri = new Uri(Options.Value.ElasticSearchUri),
            Password = Options.Value.ElasticSearchUserName,
            UserName = Options.Value.ElasticSearchUserPassword,
        };
        return Task.FromResult(result);
    }
}
