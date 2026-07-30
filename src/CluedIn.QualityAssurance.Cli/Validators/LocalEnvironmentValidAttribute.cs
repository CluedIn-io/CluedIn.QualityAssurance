using CluedIn.QualityAssurance.Cli.Environments;
using System.ComponentModel.DataAnnotations;

namespace CluedIn.QualityAssurance.Cli.Validators;

[AttributeUsage(AttributeTargets.Class)]
internal class LocalEnvironmentValidAttribute : ValidationAttributeBase
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var options = value as ILocalEnvironmentOptions;
        if (options == null || !options.IsLocalEnvironment)
        {
            return ValidationResult.Success;
        }

        return MergeResults(
            Validate<INeo4jEndpointOptions, Neo4jServerValidAttribute>(options),
            Validate<IRabbitMqEndpointOptions, RabbitMqServerValidAttribute>(options),
            Validate<ISqlServerEndpointOptions, SqlServerValidAttribute>(options),
            Validate<IElasticSearchEndpointOptions, ElasticSearchServerValidAttribute>(options),
            Validate<ICluedInServerEndpointOptions, CluedInServerValidAttribute>(options));
    }
}
