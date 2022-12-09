﻿using CluedIn.QualityAssurance.Cli.Environments;
using System.ComponentModel.DataAnnotations;

namespace CluedIn.QualityAssurance.Cli.Validators;

[AttributeUsage(AttributeTargets.Class)]
internal class SqlServerValidAttribute : ValidationAttributeBase
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var options = value as ILocalSqlServerOptions;
        if (options == null)
        {
            return ValidationResult.Success;
        }

        return MergeResults(Validate<string, RoutableUriAttribute, AbsoluteUriAttribute>($"tcp://{options.SqlServerHost}:{options.SqlServerPort}"));
    }
}
