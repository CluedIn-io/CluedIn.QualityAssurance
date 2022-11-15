using System.ComponentModel.DataAnnotations;

namespace CluedIn.QualityAssurance.Cli.Validators;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal class DirectoryExistsAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var sVal = value?.ToString() ?? "";

        if (Directory.Exists(sVal))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult($"Directory '{sVal}' does not exist");
    }
}