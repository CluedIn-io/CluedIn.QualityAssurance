using System.ComponentModel.DataAnnotations;

namespace CluedIn.QualityAssurance.Cli.Validators
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    internal class AbsoluteUriAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value == null || !Uri.TryCreate(value.ToString(), UriKind.Absolute, out _))
            {
                return new ValidationResult($"{value} is not an absolute url");
            }

            return ValidationResult.Success;
        }
    }
}
