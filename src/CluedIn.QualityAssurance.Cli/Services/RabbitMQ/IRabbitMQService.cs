using CluedIn.QualityAssurance.Cli.Models.RabbitMQ;

namespace CluedIn.QualityAssurance.Cli.Services.RabbitMQ
{
    internal interface IRabbitMQService
    {
        Task DeleteQueueAsync(string queueName, CancellationToken cancellationToken);
        Task<ICollection<QueueInfo>> GetRabbitAllQueueInfoAsync(CancellationToken cancellationToken);
        Task<QueueInfo> GetRabbitQueueInfoAsync(string queueName, CancellationToken cancellationToken);
        Task PurgeQueueAsync(string queueName, CancellationToken cancellationToken);
    }
}