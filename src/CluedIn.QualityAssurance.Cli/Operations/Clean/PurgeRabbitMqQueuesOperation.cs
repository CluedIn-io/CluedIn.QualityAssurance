using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CluedIn.QualityAssurance.Cli.Services.RabbitMQ;

using Microsoft.Extensions.Logging;

namespace CluedIn.QualityAssurance.Cli.Operations.Clean;

internal class PurgeRabbitMqQueuesOperation : Operation<PurgeRabbitMqQueuesOptions>
{
    public PurgeRabbitMqQueuesOperation(
        ILogger<PurgeRabbitMqQueuesOperation> logger,
        IRabbitMQCompletionChecker completionChecker)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        CompletionChecker = completionChecker ?? throw new ArgumentNullException(nameof(completionChecker));
    }
    private ILogger<PurgeRabbitMqQueuesOperation> Logger { get; }
    private IRabbitMQCompletionChecker CompletionChecker { get; }

    public override async Task ExecuteAsync(PurgeRabbitMqQueuesOptions options, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Begin purging all rabbitmq queues.");
        await CompletionChecker.PurgeAllQueuesAsync(cancellationToken).ConfigureAwait(false);
        Logger.LogInformation("End purging all rabbitmq queues.");
    }
}
