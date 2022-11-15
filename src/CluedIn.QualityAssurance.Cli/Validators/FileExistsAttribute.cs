using System.ComponentModel.DataAnnotations;

namespace CluedIn.QualityAssurance.Cli.Validators;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal class FileExistsAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if(value == null)
            return ValidationResult.Success;

        var sVal = value.ToString();

        if (File.Exists(sVal))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult($"File '{sVal}' does not exist");
    }
}