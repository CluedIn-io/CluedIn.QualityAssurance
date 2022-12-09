namespace CluedIn.QualityAssurance.Cli.Models.ElasticSearch;

internal class ElasticSearchConnectionInfo
{
    public Uri ServerUri { get; set; }

    public string UserName { get; set; }

    public string Password { get; set; }
}