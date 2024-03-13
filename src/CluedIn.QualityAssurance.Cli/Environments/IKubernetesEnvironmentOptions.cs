using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Environments;

internal interface IKubernetesEnvironmentOptions
{
    [Option("server-url", Default = "", Required = false, SetName = nameof(IKubernetesEnvironmentOptions), HelpText = "CluedIn Uri.")]
    string ServerUrl { get; set; }

    [Option("context-name", Default = "", Required = false, SetName = nameof(IKubernetesEnvironmentOptions), HelpText = "Kubernetes context name.")]
    string ContextName { get; set; }

    [Option("namespace", Default = "cluedin", SetName = nameof(IKubernetesEnvironmentOptions), Required = false, HelpText = "Kubernetes namespace name.")]
    string Namespace { get; set; }

    [Option("kubeconfig-path", Default = "", Required = false, SetName = nameof(IKubernetesEnvironmentOptions), HelpText = "Kubernetes context name.")]
    string KubeConfigPath { get; set; }

    [Option("kubernetes", SetName = nameof(IKubernetesEnvironmentOptions), HelpText = "Determines that this is a CluedIn in Kubernetes environment.")]
    public bool IsKubernetesEnvironment { get; set; }
}
