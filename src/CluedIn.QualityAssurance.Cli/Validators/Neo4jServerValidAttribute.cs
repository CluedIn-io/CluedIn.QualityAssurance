using CluedIn.QualityAssurance.Cli.Environments;
using System.ComponentModel.DataAnnotations;

namespace CluedIn.QualityAssurance.Cli.Validators;

[AttributeUsage(AttributeTargets.Class)]
internal class Neo4jServerValidAttribute : ValidationAttributeBase
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var options = value as ILocalNeo4jOptions;
        if (options == null)
        {
            return ValidationResult.Success;
        }

        return MergeResults(Validate<string, RoutableUriAttribute, AbsoluteUriAttribute>(options.Neo4jBoltUri));
    }
}
