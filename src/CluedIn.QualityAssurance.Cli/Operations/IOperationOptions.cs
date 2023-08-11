using CommandLine;
using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations;

internal interface IOperationOptions
{
    [Option("log-level", Default = LogLevel.Information, HelpText = "Verbosity of logging messages.")]
    LogLevel LogLevel { get; set; }

    [Option("log-file", Default = null, HelpText = "Output log file path. Logs will only be written to console if this is not set.")]
    string? LogFilePath { get; set; }
}
