using System.Text.Json.Serialization;

namespace CluedIn.QualityAssurance.Cli.Models.RabbitMQ;

internal class BackingQueueStatus
{
    [JsonPropertyName("len")]
    public long Len { get; set; }
}