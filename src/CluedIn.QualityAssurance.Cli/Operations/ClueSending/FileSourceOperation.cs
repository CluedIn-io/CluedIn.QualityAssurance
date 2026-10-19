using CluedIn.QualityAssurance.Cli.Environments;
using CluedIn.QualityAssurance.Cli.Services.PostOperationActions;
using CluedIn.QualityAssurance.Cli.Services.Probes;
using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;
using CluedIn.QualityAssurance.Cli.Services.ResultWriters;

using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal abstract partial class FileSourceOperation<TOptions> : DataSourceOperation<TOptions>
    where TOptions : IClueSendingOperationOptions, IFileSourceOperationOptions
{
    public FileSourceOperation(
        ILogger<FileSourceOperation<TOptions>> logger,
        IEnvironment testEnvironment,
        IEnumerable<IResultWriter> resultWriters,
        IRabbitMQCompletionChecker rabbitMQCompletionChecker,
        IEnumerable<IPostOperationAction> postOperationActions,
        IHttpClientFactory httpClientFactory,
        IProbeService probeService)
        : base(logger, testEnvironment, resultWriters, rabbitMQCompletionChecker, postOperationActions, httpClientFactory, probeService)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    private ILogger<FileSourceOperation<TOptions>> Logger { get; }
}
