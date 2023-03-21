using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Validators;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

[LocalEnvironmentValid]
[KubernetesEnvironmentValid]
internal abstract class ClueSendingOperationOptionsBase : IClueSendingOperationOptions, ILocalEnvironmentOptions, IKubernetesEnvironmentOptions
{
    #region CluedInServer
    public string AuthApiUrl { get; set; }

    public string PublicApiUrl { get; set; }

    public string WebApiUrl { get; set; }

    public string UiGraphqlUrl { get; set; }

    public string UploadApiUrl { get; set; }

    public string ServerUrl { get; set; }

    public string NewAccountAccessKey { get; set; }
    #endregion


    public string ClientIdPrefix { get; set; }

    public string UserName { get; set; }

    public string Password { get; set; }

    public int TotalIterations { get; set; }

    public int TimeoutInMinutes { get; set; }

    public bool IsReingestion { get; set; }

    [DirectoryExists]
    public string OutputDirectory { get; set; }

    #region Kubernetes
    public string ContextName { get; set; }

    public string Namespace { get; set; }
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
    public bool IsHomeDev { get; set; }

    public bool IsKubernetesEnvironment { get; set; }

    public bool SkipPostOperationActions { get; set; }

    public LogLevel LogLevel { get; set; }
}
