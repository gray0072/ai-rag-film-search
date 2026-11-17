using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace FilmSearch.AI.QueryUnderstanding;

/// <summary>
/// The JSON the model must return. The JSON Schema sent with the request (structured output) is generated from this
/// record, so the C# type and the schema cannot drift apart; only the allowed genre slugs are added at run time.
/// </summary>
internal sealed record QueryUnderstandingOutput(
    [property: Description("What the movie should be about, in English, without the parts that became filters. Empty string when nothing is left.")]
    string SemanticQuery,
    QueryUnderstandingFiltersOutput Filters,
    [property: Description("Titles of movies the user wants something similar to, as written.")]
    IReadOnlyList<string> SimilarToTitles,
    [property: Description("Language of the query: en, ru or sv; null for other languages.")]
    string? DetectedLocale);

internal sealed record QueryUnderstandingFiltersOutput(
    [property: Description("Genre slugs the user asks for.")]
    IReadOnlyList<string> GenresInclude,
    [property: Description("Genre slugs the user does not want.")]
    IReadOnlyList<string> GenresExclude,
    [property: Description("First release year, inclusive.")]
    int? YearFrom,
    [property: Description("Last release year, inclusive.")]
    int? YearTo,
    [property: Description("Minimum user rating, 0-10.")]
    decimal? MinRating,
    [property: Description("Maximum price in cents.")]
    long? MaxPriceMinor,
    [property: Description("ISO 639-1 code of the original language.")]
    string? Language);

internal static class QueryUnderstandingSchema
{
    public const string Name = "query_understanding";

    /// <summary>camelCase like the rest of the API; numbers in strings are accepted (small models sometimes quote them).</summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    /// <summary>
    /// JSON Schema of <see cref="QueryUnderstandingOutput"/>: every property required and no extra properties (what
    /// providers with strict structured output expect), genre arrays limited to <paramref name="genreSlugs"/> by an
    /// <c>enum</c>. A provider with constrained decoding (LM Studio, OpenAI) then cannot return an unknown genre.
    /// </summary>
    public static JsonElement Create(IReadOnlyCollection<string> genreSlugs)
    {
        var schema = AIJsonUtilities.CreateJsonSchema(
            typeof(QueryUnderstandingOutput),
            serializerOptions: JsonOptions,
            inferenceOptions: new AIJsonSchemaCreateOptions
            {
                TransformOptions = new AIJsonSchemaTransformOptions { RequireAllProperties = true, DisallowAdditionalProperties = true },
            });

        var node = JsonNode.Parse(schema.GetRawText())!.AsObject();
        var filters = node["properties"]!["filters"]!["properties"]!;
        foreach (var name in new[] { "genresInclude", "genresExclude" })
        {
            filters[name]!["items"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray([.. genreSlugs.Select(s => JsonValue.Create(s))]),
            };
        }

        return JsonSerializer.SerializeToElement(node);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        options.MakeReadOnly();
        return options;
    }
}
