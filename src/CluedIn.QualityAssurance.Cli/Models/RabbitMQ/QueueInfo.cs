namespace CluedIn.QualityAssurance.Cli.Models.RabbitMQ;

internal record QueueMessageInfo(uint Count, double Rate);
internal record QueueInfo(
    string QueueName,
    QueueMessageInfo Acknowledged,
    QueueMessageInfo Delivered,
    QueueMessageInfo DeliveredOrGet,
    QueueMessageInfo DeliveredNoAck,
    QueueMessageInfo Get,
    QueueMessageInfo GetEmpty,
    QueueMessageInfo GetNoAck,
    QueueMessageInfo Redelivered,
    QueueMessageInfo Published,
    QueueMessageInfo Messages,
    DateTimeOffset PolledAt);
