using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FilmSearch.Core.Ingestion;

/// <summary>JSON settings of the seed file, shared by the seed builder (writes) and the seed loader (reads).</summary>
public static class SeedJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Keep Cyrillic and Swedish letters readable in the committed file (and in diffs).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
