using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations;

internal interface IMultiIterationOptions : IOperationOptions
{
    [Option('n', "number-of-runs", Default = 1, Required = false, HelpText = "Number of test runs.")]
    int TotalIterations { get; set; }

    [Option('t', "timeout", Default = 10, Required = false, HelpText = "The timeout for each iteration, in minutes.")]
    int TimeoutInMinutes { get; set; }
}
