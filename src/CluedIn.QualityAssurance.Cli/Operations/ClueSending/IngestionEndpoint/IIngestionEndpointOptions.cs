using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.IngestionEndpoint;

internal interface IIngestionEndpointOptions : IFileSourceOperationOptions
{
    [Option("use-ingestion-endpoint", Default = false, Required = false, HelpText = "Create and use an ingestion endpoint to stream data.")]
    bool UseIngestionEndpoint { get; set; }

    [Option("ingestion-batch-size", Default = 200, Required = false, HelpText = "Ingestion batch size.")]
    int IngestionBatchSize { get; set; }

    [Option("delay-between-ingestion-requests", Default = 500, Required = false, HelpText = "The delay between multiple requests to ingestion endpoint, in milliseconds.")]
    int IngestionRequestsDelayInMilliseconds { get; set; }
}
