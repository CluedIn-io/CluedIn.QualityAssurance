namespace CluedIn.QualityAssurance.Cli.Models.SqlServer;

internal record SqlServerConnectionInfo(string Host, int Port, string UserName, string Password)
{
    public string CreateConnectionString()
    {
        return $"Server={Host},{Port};User Id={UserName};Password={Password};TrustServerCertificate=True;";
    }
}
