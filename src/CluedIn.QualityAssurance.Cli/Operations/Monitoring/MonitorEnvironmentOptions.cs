using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Validators;

using CommandLine;

using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations.Monitoring;

[Verb("monitor-environment", HelpText = "Monitor Environment")]
internal class MonitorEnvironmentOptions : IOperationOptions, ILocalEnvironmentOptions, IKubernetesEnvironmentOptions
{
    #region CluedInServer
    public string AuthApiUrl { get; set; }

    public string PublicApiUrl { get; set; }

    public string WebApiUrl { get; set; }

    public string UiGraphqlUrl { get; set; }

    public string UploadApiUrl { get; set; }

    public string IngestApiUrl { get; set; }

    public string ServerUrl { get; set; }

    public string NewAccountAccessKey { get; set; }
    #endregion

    #region Kubernetes
    public string ContextName { get; set; }

    public string Namespace { get; set; }

    public string KubeConfigPath { get; set; }
    #endregion

    #region RabbitMQ
    public string RabbitMQManagementUri { get; set; }

    public string RabbitUserName { get; set; }

    public string RabbitUserPassword { get; set; }
    #endregion

    #region Neo4j
    public string Neo4jBoltUri { get; set; }

    public string Neo4jUserName { get; set; }

    public string Neo4jUserPassword { get; set; }
    #endregion

    #region SqlServer
    public string SqlServerHost { get; set; }

    public int SqlServerPort { get; set; }

    public string SqlServerUserName { get; set; }

    public string SqlServerUserPassword { get; set; }
    #endregion

    #region ElasticSearch
    public string ElasticSearchUri { get; set; }

    public string ElasticSearchUserName { get; set; }

    public string ElasticSearchUserPassword { get; set; }
    #endregion

    public bool IsLocalEnvironment { get; set; }

    public bool IsKubernetesEnvironment { get; set; }

    public bool ShouldCheckForServiceOverrides { get; set; }

    public LogLevel LogLevel { get; set; }

    public string? LogFilePath { get; set; }

    [DirectoryExists]
    [Option("output-directory", Required = true, HelpText = "Result output directory.")]
    public string OutputDirectory { get; set; }

    [Option("create-subdirectory-for-output", Required = false, HelpText = "Create subdirectory for output.")]
    public bool ShouldCreateSubdirectoryForOutput { get; set; }
}
