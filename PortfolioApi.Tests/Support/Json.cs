using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortfolioApi.Tests.Support;

/// <summary>Reads API responses with the same JSON settings as the API (camelCase, enums as text).</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Options))!;
}
