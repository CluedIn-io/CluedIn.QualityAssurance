namespace CluedIn.QualityAssurance.Cli.Models.Neo4j;

internal record Neo4jConnectionInfo(Uri BoltUri, string UserName, string Password);