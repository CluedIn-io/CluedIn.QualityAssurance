using CluedIn.QualityAssurance.Cli.Environments;
using System.ComponentModel.DataAnnotations;

namespace CluedIn.QualityAssurance.Cli.Validators;

[AttributeUsage(AttributeTargets.Class)]
internal class CluedInServerValidAttribute : ValidationAttributeBase
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var options = value as ILocalCluedInServerOptions;
        if (options == null)
        {
            return ValidationResult.Success;
        }

        return MergeResults(
            Validate<string, RoutableUriAttribute, AbsoluteUriAttribute>(options.UiGraphqlUrl),
            Validate<string, RoutableUriAttribute, AbsoluteUriAttribute>(options.PublicApiUrl),
            Validate<string, RoutableUriAttribute, AbsoluteUriAttribute>(options.WebApiUrl),
            Validate<string, RoutableUriAttribute, AbsoluteUriAttribute>(options.UploadApiUrl),
            Validate<string, RoutableUriAttribute, AbsoluteUriAttribute>(options.AuthApiUrl));
    }
}