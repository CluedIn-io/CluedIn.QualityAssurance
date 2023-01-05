namespace CluedIn.QualityAssurance.Cli.Models.SqlServer;

internal class SqlServerConnectionInfo
{
    public string Host { get; set; }

    public int Port { get; set; }

    public string UserName { get; set; }

    public string Password { get; set; }

    public string CreateConnectionString()
    {
        return $"Server={Host},{Port};User Id={UserName};Password={Password};TrustServerCertificate=True;";
    }
}
