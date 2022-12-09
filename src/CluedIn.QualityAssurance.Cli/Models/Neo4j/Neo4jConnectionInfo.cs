namespace CluedIn.QualityAssurance.Cli.Models.Neo4j;

internal class Neo4jConnectionInfo
{
    public Uri BoltUri { get; set; }

    public string UserName { get; set; }

    public string Password { get; set; }
}

