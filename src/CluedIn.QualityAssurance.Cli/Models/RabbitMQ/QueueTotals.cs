using System.Text.Json.Serialization;

namespace CluedIn.QualityAssurance.Cli.Models.RabbitMQ;

internal class QueueTotals
{
    [JsonPropertyName("messages")]
    public long Messages { get; set; }

    [JsonPropertyName("messages_ready")]
    public long MessagesReady { get; set; }
}