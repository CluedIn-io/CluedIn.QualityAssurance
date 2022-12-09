using System.Text.Json.Serialization;

namespace CluedIn.QualityAssurance.Cli.Models.RabbitMQ;

internal class RabbitOverview
{
    [JsonPropertyName("queue_totals")]
    public QueueTotals QueueTotals { get; set; }
}
