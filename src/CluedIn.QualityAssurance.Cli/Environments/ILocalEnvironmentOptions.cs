using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Environments;

internal interface ILocalNeo4jOptions
{
    [Option("neo4j-bolt-uri", Default = "bolt://localhost:7687", Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "Neo4j Bolt Uri.")]
    string Neo4jBoltUri { get; set; }

    [Option("neo4j-username", Default = "neo4j", Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "Neo4j username.")]
    string Neo4jUserName { get; set; }

    [Option("neo4j-password", Default = "password", Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "Neo4j password.")]
    string Neo4jUserPassword { get; set; }
}

internal interface ILocalRabbitMqOptions
{
    [Option("rabbitmq-management-uri", Default = "http://localhost:15672/", Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "RabbitMQ Management Uri. If credentials are required, it should NOT be included.")]
    string RabbitMQManagementUri { get; set; }

    [Option("rabbitmq-username", Default = "guest", Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "RabbitMQ username.")]
    string RabbitUserName { get; set; }

    [Option("rabbitmq-password", Default = "guest", Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "RabbitMQ password.")]
    string RabbitUserPassword { get; set; }
}
internal interface ILocalSqlServerOptions
{
    [Option("sqlserver-host", Default = "localhost", Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "Sql Server host.")]
    string SqlServerHost { get; set; }

    [Option("sqlserver-port", Default = 1433, Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "Sql Server port")]
    int SqlServerPort { get; set; }

    [Option("sqlserver-username", Default = "sa", Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "Sql Server username.")]
    string SqlServerUserName { get; set; }

    [Option("sqlserver-password", Default = "yourStrong(!)Password", Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "Sql Server password.")]
    string SqlServerUserPassword { get; set; }
}

internal interface ILocalElasticSearchOptions
{
    [Option("elasticsearch-uri", Default = "http://localhost:9200", Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "Elastic Search Uri.")]
    string ElasticSearchUri { get; set; }

    [Option("elasticsearch-username", Default = "", Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "Elastic Search username.")]
    string ElasticSearchUserName { get; set; }

    [Option("elasticsearch-password", Default = "", Required = false, SetName = nameof(ILocalEnvironmentOptions), HelpText = "Elastic Search password.")]
    string ElasticSearchUserPassword { get; set; }
}

internal interface ILocalCluedInServerOptions
{
    [Option("auth-api-url", Default = "http://localhost:9001/", SetName = nameof(ILocalEnvironmentOptions), HelpText = "CluedIn Server Auth API Uri.")]
    string AuthApiUrl { get; set; }

    [Option("public-api-url", Default = "http://localhost:9007/", SetName = nameof(ILocalEnvironmentOptions), HelpText = "CluedIn Server Public API Uri.")]
    string PublicApiUrl { get; set; }

    [Option("web-api-url", Default = "http://localhost:9000/", SetName = nameof(ILocalEnvironmentOptions), HelpText = "CluedIn Server Web API Uri.")]
    string WebApiUrl { get; set; }

    [Option("ui-graphql-url", Default = "http://localhost:8888/graphql", SetName = nameof(ILocalEnvironmentOptions), HelpText = "CluedIn UI GraphQL Uri.")]
    string UiGraphqlUrl { get; set; }

    [Option("upload-api-url", Default = "http://localhost:8888/upload", SetName = nameof(ILocalEnvironmentOptions), HelpText = "CluedIn Upload API Uri.")]
    string UploadApiUrl { get; set; }
}

internal interface ILocalEnvironmentOptions : ILocalNeo4jOptions, ILocalRabbitMqOptions, ILocalSqlServerOptions, ILocalElasticSearchOptions, ILocalCluedInServerOptions
{
    [Option("local", SetName = nameof(ILocalEnvironmentOptions), HelpText = "Determines that this is a CluedIn in local environment.")]
    public bool IsLocalEnvironment { get; set; }
}
