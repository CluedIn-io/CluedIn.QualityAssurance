using System.Text.Json;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal static class GraphqlQueryHelper
{
    public static string Serialize(string query)
    {
        return JsonSerializer.Serialize(query);
    }
}
