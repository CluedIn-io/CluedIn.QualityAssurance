using System.Text.Json.Serialization;

namespace CluedIn.QualityAssurance.Cli.Models.RabbitMQ;

internal class RabbitQueue
{
    [JsonPropertyName("backing_queue_status")]
    public BackingQueueStatus BackingQueueStatus { get; set; }
}