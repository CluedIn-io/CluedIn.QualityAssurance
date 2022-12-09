using System.Net.Http.Json;
using System.Text.Json;

namespace CluedIn.QualityAssurance.Cli;

internal static partial class JsonSerializerExtensions
{
    public static async Task<T?> DeserializeToAnonymousTypeAsync<T>(this HttpContent httpContent, T anonymousTypeObject, JsonSerializerOptions? options = null)
    {
        return await httpContent.ReadFromJsonAsync<T>(options).ConfigureAwait(false);
    }

    public static T? DeserializeToAnonymousType<T>(this string json, T anonymousTypeObject, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(json, options);
    }
}
