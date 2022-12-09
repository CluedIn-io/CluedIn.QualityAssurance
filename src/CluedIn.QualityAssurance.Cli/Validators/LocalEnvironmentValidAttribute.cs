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
            Validate<ILocalNeo4jOptions, Neo4jServerValidAttribute>(options),
            Validate<ILocalRabbitMqOptions, RabbitMqServerValidAttribute>(options),
            Validate<ILocalSqlServerOptions, SqlServerValidAttribute>(options),
            Validate<ILocalElasticSearchOptions, ElasticSearchServerValidAttribute>(options),
            Validate<ILocalCluedInServerOptions, CluedInServerValidAttribute>(options));
    }
}
