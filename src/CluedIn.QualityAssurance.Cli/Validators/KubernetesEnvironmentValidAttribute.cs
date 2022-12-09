using CluedIn.QualityAssurance.Cli.Environments;
using System.ComponentModel.DataAnnotations;

namespace CluedIn.QualityAssurance.Cli.Validators;

[AttributeUsage(AttributeTargets.Class)]
internal class KubernetesEnvironmentValidAttribute : ValidationAttributeBase
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var options = value as IKubernetesEnvironmentOptions;
        if (options == null || !options.IsKubernetesEnvironment)
        {
            return ValidationResult.Success;
        }

        return MergeResults(
            Validate<string, RoutableUriAttribute, AbsoluteUriAttribute>(options.ServerUrl),
            Validate<string, RequiredAttribute>(options.ContextName),
            Validate<string, RequiredAttribute>(options.Namespace));
    }
}
