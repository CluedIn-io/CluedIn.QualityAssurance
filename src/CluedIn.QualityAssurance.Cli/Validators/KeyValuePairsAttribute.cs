using System.ComponentModel.DataAnnotations;

namespace CluedIn.QualityAssurance.Cli.Validators
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    internal class KeyValuePairsAttribute : ValidationAttribute
    {
        private char KeyValueSeparator { get; set; } = '=';

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value == null || !(value is string[] keyValuePairs))
            {
                return CreateInvalidValueResult(value);
            }

            foreach(var keyValuePair in keyValuePairs)
            {
                if (string.IsNullOrWhiteSpace(keyValuePair) || keyValuePair.Split(KeyValueSeparator).Count() != 2)
                {
                    return CreateInvalidKeyValuePairResult(keyValuePair);
                }
            }

            return ValidationResult.Success;
        }

        private static ValidationResult CreateInvalidValueResult(object? value)
        {
            return new ValidationResult($"{value} is not key value pairs in the form of key1=value1,key2=value2 ('=' in key or value is not supported).");
        }

        private static ValidationResult CreateInvalidKeyValuePairResult(string keyValuePair)
        {
            return new ValidationResult($"{keyValuePair} is not key value pairs in the form of key1=value1 ('=' in key or value is not supported).");
        }
    }
}
