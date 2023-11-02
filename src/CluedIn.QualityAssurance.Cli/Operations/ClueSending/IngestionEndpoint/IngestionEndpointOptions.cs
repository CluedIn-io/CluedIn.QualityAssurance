using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.IngestionEndpoint;

[Verb("ingestion-endpoint", HelpText = "Perform test using ingestion endpoint.")]
internal class IngestionEndpointOptions : FileSourceOperationOptions, IIngestionEndpointOptions
{
    public bool UseIngestionEndpoint { get; set; }

    public int IngestionBatchSize { get; set; }

    public int IngestionRequestsDelayInMilliseconds { get; set; }
}
