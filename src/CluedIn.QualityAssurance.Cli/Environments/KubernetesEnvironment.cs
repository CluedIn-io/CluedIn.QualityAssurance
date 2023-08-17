using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using CluedIn.QualityAssurance.Cli.Models.ElasticSearch;
using CluedIn.QualityAssurance.Cli.Models.Neo4j;
using CluedIn.QualityAssurance.Cli.Models.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Models.SqlServer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CluedIn.QualityAssurance.Cli.Environments;

internal class KubernetesEnvironment : IEnvironment
{
    private static readonly TimeSpan PortForwardConnectionAliveInterval = TimeSpan.FromMinutes(30);
    private static readonly Dictionary<string, ServiceSettings> Settings = new Dictionary<string, ServiceSettings>
    {
        [nameof(Neo4jConnectionInfo)] = new ServiceSettings("neo4j", "cluedin-neo4j", "cluedin-neo4j-secrets", "neo4j-password", 7687),
        [nameof(RabbitMQConnectionInfo)] = new ServiceSettings("cluedin", "cluedin-rabbitmq", "cluedin-rabbitmq", "rabbitmq-password", 15672),
        [nameof(SqlServerConnectionInfo)] = new ServiceSettings("sa", "cluedin-sqlserver", "cluedin-sqlserver-secret", "sapassword", 1433),
        [nameof(ElasticSearchConnectionInfo)] = new ServiceSettings("elastic", "cluedin-elasticsearch", "elasticsearch-credentials", "password", 9200),
    };

    private Dictionary<string, ConnectionInfo> Connections { get; set; } = new Dictionary<string, ConnectionInfo>();

    private class ConnectionInfo
    {
        public Process Process { get; set; }
        public object Info { get; set; }
    }
    private record ServiceSettings (string UserName, string ServiceName, string SecretName, string PasswordField, int Port);

    public KubernetesEnvironment(ILogger<KubernetesEnvironment> logger, IOptions<IKubernetesEnvironmentOptions> options)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private ILogger<KubernetesEnvironment> Logger { get; }
    private IOptions<IKubernetesEnvironmentOptions> Options { get; }
    private string NewAccountAccessKey { get; set; }
    private DateTimeOffset SetupTime { get; set; }

    public Task<float> GetAvailableMemoryInMegabytesAsync(CancellationToken cancellationToken)
    {
        Logger.LogWarning("Memory retrieval is not implemented yet.");
        return Task.FromResult(-1.0f);
    }

    private async Task<T> GetOrCreateConnectionInfoAsync<T>(string name, Func<string, string, PortForwardResult, T> createConnectionInfoFunc, CancellationToken cancellationToken)
        where T : class
    {
        if (TryGetConnectionInfo<T>(name, out var foundConnectionInfo, out var settings))
        {
            return foundConnectionInfo;
        }

        var result = await PortForwardAsync(settings.ServiceName, settings.Port, cancellationToken);
        var password = await GetPasswordAsync(settings.SecretName, settings.PasswordField, cancellationToken);

        var connectionInfo = createConnectionInfoFunc(settings.UserName, password, result);
        Connections.Add(name, new ConnectionInfo
        {
            Info= connectionInfo,
            Process = result.Process,
        });
        return connectionInfo;
    }

    private async Task<T> GetConnectionInfoAsync<T>(string name, CancellationToken cancellationToken)
        where T : class
    {
        if (DateTimeOffset.UtcNow - SetupTime > PortForwardConnectionAliveInterval)
        {
            await SetupAsync(cancellationToken);
        }

        if (TryGetConnectionInfo<T>(name, out var foundConnectionInfo, out _))
        {
            return foundConnectionInfo;
        }

        throw new InvalidOperationException($"Retrieved null connectionInfo '{name}'.");
    }

    private bool TryGetConnectionInfo<T>(string name, [NotNullWhen(true)] out T? connectionInfo, out ServiceSettings settings)
        where T : class
    {
        if (!Settings.TryGetValue(name, out settings))
        {
            throw new NotSupportedException($"Invalid setting name '{name}'.");
        }

        if (Connections.TryGetValue(name, out var foundInfo))
        {
            var found = foundInfo.Info as T;
            if (found == null)
            {
                throw new InvalidOperationException($"Found null info for connection with name '{name}'.");
            }
            connectionInfo = found;
            return true;
        }

        connectionInfo = null;
        return false;
    }

    public async Task SetupAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Setting up kubernetes environment for testing.");
        Connections.Clear();

        _ = await this.GetOrCreateConnectionInfoAsync(
            nameof(RabbitMQConnectionInfo),
            (userName, password, portForwardResult) => new RabbitMQConnectionInfo
            {
                ManagementUri = new Uri($"http://{portForwardResult.Uri}"),
                Password = password,
                UserName = userName,
            },
            cancellationToken);

        _ = await this.GetOrCreateConnectionInfoAsync(
            nameof(Neo4jConnectionInfo),
            (userName, password, portForwardResult) => new Neo4jConnectionInfo
            {
                BoltUri = new Uri($"bolt://{portForwardResult.Uri}"),
                Password = password,
                UserName = userName,
            },
            cancellationToken);

        _ = await this.GetOrCreateConnectionInfoAsync(
            nameof(ElasticSearchConnectionInfo),
            (userName, password, portForwardResult) => new ElasticSearchConnectionInfo
            {
                ServerUri = new Uri($"http://{portForwardResult.Uri}"),
                Password = password,
                UserName = userName,
            },
            cancellationToken);


        _ = await this.GetOrCreateConnectionInfoAsync(
            nameof(SqlServerConnectionInfo),
            (userName, password, portForwardResult) => {
                var temp = new Uri($"tcp://{portForwardResult.Uri}");
                return new SqlServerConnectionInfo
                {
                    Host = temp.Host,
                    Port = temp.Port,
                    Password = password,
                    UserName = userName,
                };
            },
            cancellationToken);

        NewAccountAccessKey = await this.GetNewAccountAccessKeyInternalAsync(cancellationToken).ConfigureAwait(false);
        SetupTime = DateTimeOffset.UtcNow;
    }

    private async Task<PortForwardResult> PortForwardAsync(string serviceName, int port, CancellationToken cancellationToken)
    {
        var runner = new KubectlRunner();
        var taskCompletionSource = new TaskCompletionSource<PortForwardResult>();
        var arguments = new string[]
        {
                "port-forward",
                $"service/{serviceName}",
                $":{port}",
        };

        if (!string.IsNullOrWhiteSpace(Options.Value.KubeConfigPath))
        {
            arguments = arguments.Concat(new string[] { "--kubeconfig", Options.Value.KubeConfigPath }).ToArray();
        }

        runner.PortForwardAsync(
            Directory.GetCurrentDirectory(),
            AppendEnvironmentDetails(arguments),
            taskCompletionSource,
            cancellationToken);

        var result = await taskCompletionSource.Task;

        if (result.Uri == default)
        {
            throw new InvalidOperationException("Retrieved null Neo4jConnectionInfo.");
        }

        return result;
    }

    private string[] AppendEnvironmentDetails(IEnumerable<string> arguments)
    {
        if (!string.IsNullOrWhiteSpace(Options.Value.Namespace))
        {
            arguments = arguments.Append($"--namespace {Options.Value.Namespace}");
        }
        if (!string.IsNullOrWhiteSpace(Options.Value.ContextName))
        {
            arguments = arguments.Append($"--context {Options.Value.ContextName}");
        }
        if (!string.IsNullOrWhiteSpace(Options.Value.KubeConfigPath))
        {
            arguments = arguments.Append($"--kubeconfig {Options.Value.KubeConfigPath}");
        }

        return arguments.ToArray();
    }

    private async Task<string> GetPasswordAsync(string secretName, string passwordField, CancellationToken cancellationToken)
    {
        Logger.LogDebug("Retrieving secret '{secretName}'.", secretName);
        var runner = new KubectlRunner();
        var arguments = new string[]
        {
            "get",
            $"secrets/{secretName}",
            "-o json"
        };
        var result = await runner.RunAsync(
            Directory.GetCurrentDirectory(),
            AppendEnvironmentDetails(arguments),
            cancellationToken).ConfigureAwait(false);

        var response = result.Output.DeserializeToAnonymousType(new
        {
            data = (Dictionary<string, string>?)null,
        });

        var base64Password = response?.data?[passwordField] ?? throw new InvalidOperationException($"Failed to get field '{passwordField}'.");

        var decodedPassword = Encoding.UTF8.GetString(Convert.FromBase64String(base64Password));

        // TODO: remove logging of password
        Logger.LogDebug("Retrieved secret '{secretName}'. Value is {password}", secretName, decodedPassword);
        return decodedPassword;
    }

    public Task TearDownAsync(CancellationToken cancellationToken)
    {
        foreach (var connection in Connections)
        {
            connection.Value.Process.Kill(true);
        }
        return Task.CompletedTask;
    }

    public Task<ServerUriCollection> GetServerUriCollectionAsync(CancellationToken cancellationToken)
    {
        var serverUrlWithTrailingSlash = EnsureTrailingSlash(new Uri(Options.Value.ServerUrl));
        return Task.FromResult(new ServerUriCollection(
            AuthApiUri: new Uri(serverUrlWithTrailingSlash, "auth/"),
            PublicApiUri: new Uri(serverUrlWithTrailingSlash, "public/"),
            WebApiUri: new Uri(serverUrlWithTrailingSlash, "api/"),
            UiGraphqlUri: new Uri(serverUrlWithTrailingSlash, "graphql/"),
            UploadApiUri: new Uri(serverUrlWithTrailingSlash, "upload/")
            ));
    }

    private async Task<string> GetNewAccountAccessKeyInternalAsync(CancellationToken cancellationToken)
    {
        return await GetPasswordAsync("cluedin-new-organization-access-key", "key", cancellationToken).ConfigureAwait(false);
    }

    public Task<string> GetNewAccountAccessKeyAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(NewAccountAccessKey);
    }

    private Uri EnsureTrailingSlash(Uri uri)
    {
        if (!uri.AbsoluteUri.EndsWith("/"))
        {
            return new Uri(uri.AbsoluteUri + "/");
        }

        return uri;
    }

    public Task<RabbitMQConnectionInfo> GetRabbitMqConnectionInfoAsync(CancellationToken cancellationToken)
    {
        return GetConnectionInfoAsync<RabbitMQConnectionInfo>(nameof(RabbitMQConnectionInfo), cancellationToken);
    }

    public Task<Neo4jConnectionInfo> GetNeo4jConnectionInfoAsync(CancellationToken cancellationToken)
    {
        return GetConnectionInfoAsync<Neo4jConnectionInfo>(nameof(Neo4jConnectionInfo), cancellationToken);
    }

    public Task<SqlServerConnectionInfo> GetSqlServerConnectionInfoAsync(CancellationToken cancellationToken)
    {
        return GetConnectionInfoAsync<SqlServerConnectionInfo>(nameof(SqlServerConnectionInfo), cancellationToken);
    }

    public Task<ElasticSearchConnectionInfo> GetElasticSearchConnectionInfoAsync(CancellationToken cancellationToken)
    {
        return GetConnectionInfoAsync<ElasticSearchConnectionInfo>(nameof(ElasticSearchConnectionInfo), cancellationToken);
    }
}
