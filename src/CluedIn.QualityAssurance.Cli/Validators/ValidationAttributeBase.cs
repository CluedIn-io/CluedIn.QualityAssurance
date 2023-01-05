using System.ComponentModel.DataAnnotations;

namespace CluedIn.QualityAssurance.Cli.Validators;

internal abstract class ValidationAttributeBase : ValidationAttribute
{
    protected virtual ValidationResult MergeResults(params TryValidateResult[] tryValidationResults)
    {
        var isValid = tryValidationResults.All(result => result.IsValid);
        if (isValid)
        {
            return ValidationResult.Success;
        }

        var results = tryValidationResults.SelectMany(result => result.ValidationResults);

        return new ValidationResult(string.Join(Environment.NewLine, results.Select(x => x.ErrorMessage)));
    }

    protected virtual TryValidateResult Validate<TValue>(TValue value, params ValidationAttribute[] validationAttributes)
    {
        var context = new ValidationContext(value);

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateValue(value, context, results, validationAttributes);

        return new(isValid, results);
    }


    protected virtual TryValidateResult Validate<TValue, TValidationAttribute1>(TValue value)
        where TValidationAttribute1 : ValidationAttribute, new()
    {
        return Validate(value, new TValidationAttribute1());
    }

    protected virtual TryValidateResult Validate<TValue, TValidationAttribute1, TValidationAttribute2>(TValue value)
        where TValidationAttribute1 : ValidationAttribute, new()
        where TValidationAttribute2 : ValidationAttribute, new()
    {
        return Validate(value, new TValidationAttribute1(), new TValidationAttribute2());
    }

    protected record TryValidateResult(bool IsValid, ICollection<ValidationResult> ValidationResults);
}
