using CommandLine;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations;

internal interface IOperationOptions
{
    [Option("log-level", Default = LogLevel.Information, HelpText = "Verbosity of logging messages.")]
    LogLevel LogLevel { get; set; }
}
