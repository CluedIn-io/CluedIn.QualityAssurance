namespace CluedIn.QualityAssurance.Cli.Models.RabbitMQ;

internal class RabbitMQConnectionInfo
{
    public Uri ManagementUri { get; set; }

    public string UserName { get; set; }

    public string Password { get; set; }
}
