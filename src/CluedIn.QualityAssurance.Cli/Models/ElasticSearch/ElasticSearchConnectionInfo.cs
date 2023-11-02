namespace CluedIn.QualityAssurance.Cli.Models.ElasticSearch;

internal record ElasticSearchConnectionInfo(Uri ServerUri, string UserName, string Password);