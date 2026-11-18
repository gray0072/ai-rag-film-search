using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FilmSearch.Core.Assistant;
using Microsoft.Extensions.AI;

namespace FilmSearch.AI.Assistant.Tools;

/// <summary>What a tool gets from the turn besides its arguments. The owner is not here: services take it from the request (spec 06 §3).</summary>
/// <param name="ConversationId">The conversation of the turn.</param>
/// <param name="Locale">Locale of movie texts in results and cards.</param>
/// <param name="AnswerId">Id of the answer being written (a confirmation belongs to it).</param>
internal sealed record ToolContext(Guid ConversationId, string Locale, Guid AnswerId = default);

/// <summary>Result of a tool call.</summary>
/// <param name="Result">JSON for the model (compact: only the fields it needs).</param>
/// <param name="Ok">False when the arguments were refused or the tool failed; <paramref name="Result"/> then explains why.</param>
/// <param name="Movies">Cards for the UI, built from catalog data (the <c>movies</c> event).</param>
internal sealed record ToolOutcome(string Result, bool Ok, IReadOnlyList<MovieCard> Movies)
{
    /// <summary>Other events for the UI, emitted after <c>tool.completed</c> (<c>cart.updated</c>, <c>confirmation.required</c>).</summary>
    public IReadOnlyList<AssistantEvent> Events { get; init; } = [];

    public static ToolOutcome Success(object payload, IReadOnlyList<MovieCard>? movies = null) =>
        new(JsonSerializer.Serialize(payload, ToolJson.Options), true, movies ?? []);

    /// <summary>An error the model can read and react to (fix the arguments, try another tool, or tell the user).</summary>
    public static ToolOutcome Error(string message) =>
        new(JsonSerializer.Serialize(new { error = message }, ToolJson.Options), false, []);
}

/// <summary>
/// A tool of the agent (ADR-0006): the declaration (name, description, JSON Schema) goes to the model; the call is run by
/// our loop, not by middleware. Tools are thin adapters over module services (spec 02 §5).
/// </summary>
internal abstract class AssistantTool
{
    public abstract string Name { get; }

    public abstract Task<AIFunctionDeclaration> GetDeclarationAsync(CancellationToken cancellationToken);

    /// <summary>Validates the model's arguments and runs the tool. Invalid arguments give <see cref="ToolOutcome.Error"/>, not an exception.</summary>
    public abstract Task<ToolOutcome> InvokeAsync(IDictionary<string, object?>? arguments, ToolContext context, CancellationToken cancellationToken);
}

/// <summary>
/// A tool whose arguments are the C# record <typeparamref name="TArgs"/>: the JSON Schema is generated from it, so the
/// schema and the parser cannot drift apart. Arguments are checked against the schema before the tool runs: required
/// properties must be present, values must bind to their types (enums, numbers, ids). Unknown properties are ignored
/// (small models add some), and errors name the property and its allowed values, so the model can fix the call.
/// </summary>
internal abstract class AssistantTool<TArgs> : AssistantTool
    where TArgs : class
{
    private static readonly Lazy<JsonObject> BaseSchema = new(CreateBaseSchema);

    protected abstract string Description { get; }

    public override async Task<AIFunctionDeclaration> GetDeclarationAsync(CancellationToken cancellationToken)
    {
        var node = BaseSchema.Value.DeepClone().AsObject();
        await CustomizeSchemaAsync(node, cancellationToken);
        return AIFunctionFactory.CreateDeclaration(Name, Description, JsonSerializer.SerializeToElement(node));
    }

    public override async Task<ToolOutcome> InvokeAsync(IDictionary<string, object?>? arguments, ToolContext context, CancellationToken cancellationToken)
    {
        arguments ??= new Dictionary<string, object?>();
        var missing = RequiredProperties()
            .Where(p => !arguments.TryGetValue(p, out var value) || value is null || (value is JsonElement { ValueKind: JsonValueKind.Null }))
            .ToList();
        if (missing.Count > 0)
        {
            return ToolOutcome.Error(string.Join(' ', missing.Select(p => $"`{p}` is required{Hint(p)}.")));
        }

        TArgs? parsed;
        try
        {
            // Values are JsonElements from the provider (or plain values in tests): one round trip through JSON binds both.
            var json = JsonSerializer.SerializeToElement(arguments, ToolJson.Options);
            parsed = json.Deserialize<TArgs>(ToolJson.Options);
        }
        catch (JsonException ex)
        {
            var property = ex.Path is { Length: > 2 } path && path.StartsWith("$.", StringComparison.Ordinal) ? path[2..].Split('[', '.')[0] : null;
            return ToolOutcome.Error(property is null
                ? $"Invalid arguments for {Name}: {ex.Message}"
                : $"Invalid arguments for {Name}: `{property}` has an invalid value{Hint(property)}.");
        }

        return parsed is null
            ? ToolOutcome.Error($"Invalid arguments for {Name}: an object is required.")
            : await ExecuteAsync(parsed, context, cancellationToken);
    }

    /// <summary>Changes the generated schema, e.g. adds an <c>enum</c> of values known only at run time.</summary>
    protected virtual Task CustomizeSchemaAsync(JsonObject schema, CancellationToken cancellationToken) => Task.CompletedTask;

    protected abstract Task<ToolOutcome> ExecuteAsync(TArgs arguments, ToolContext context, CancellationToken cancellationToken);

    private static IEnumerable<string> RequiredProperties() =>
        BaseSchema.Value["required"] is JsonArray required ? required.Select(r => r!.GetValue<string>()) : [];

    /// <summary>": one of buy, rent" for enums, ": a movie id (uuid) from a tool result" for ids.</summary>
    private static string Hint(string property)
    {
        if (BaseSchema.Value["properties"]?[property] is not JsonObject schema)
        {
            return "";
        }

        if (schema["enum"] is JsonArray values)
        {
            return $": one of {string.Join(", ", values.Where(v => v is not null).Select(v => v!.ToString()))}";
        }

        return schema["format"]?.GetValue<string>() == "uuid" ? ": a movie id (uuid) from a tool result" : "";
    }

    private static JsonObject CreateBaseSchema()
    {
        var schema = AIJsonUtilities.CreateJsonSchema(
            typeof(TArgs),
            serializerOptions: ToolJson.Options,
            inferenceOptions: new AIJsonSchemaCreateOptions
            {
                TransformOptions = new AIJsonSchemaTransformOptions { DisallowAdditionalProperties = true },
            });

        var node = JsonNode.Parse(schema.GetRawText())!.AsObject();

        // A tool without arguments still declares an empty object: some OpenAI-compatible servers require "properties".
        node["properties"] ??= new JsonObject();
        return node;
    }
}

/// <summary>Arguments of a tool without arguments (<c>view_cart</c>, <c>request_checkout</c>).</summary>
internal sealed record NoArgs;

internal static class ToolJson
{
    /// <summary>camelCase; numbers in strings are accepted (small models sometimes quote them); nulls are left out of results.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Titles and overviews stay readable (Cyrillic, å/ä/ö) and cost fewer tokens than \uXXXX escapes.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly();
        return options;
    }
}
