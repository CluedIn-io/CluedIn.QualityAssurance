namespace CluedIn.QualityAssurance.Cli.Services.RabbitMQ;

internal interface IRabbitMQCompletionChecker
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<RabbitMQCompletionResult> PollForCompletionAsync(CancellationToken cancellationToken);
    Task PurgeAllQueuesAsync(CancellationToken cancellationToken);
    Task ValidateIdleAsync(CancellationToken cancellationToken);
}
