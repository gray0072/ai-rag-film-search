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
internal sealed record ToolContext(Guid ConversationId, string Locale);

/// <summary>Result of a tool call.</summary>
/// <param name="Result">JSON for the model (compact: only the fields it needs).</param>
/// <param name="Ok">False when the arguments were refused or the tool failed; <paramref name="Result"/> then explains why.</param>
/// <param name="Movies">Cards for the UI, built from catalog data (the <c>movies</c> event).</param>
internal sealed record ToolOutcome(string Result, bool Ok, IReadOnlyList<MovieCard> Movies)
{
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

/// <summary>A tool whose arguments are the C# record <typeparamref name="TArgs"/>: the JSON Schema is generated from it, so the schema and the parser cannot drift apart.</summary>
internal abstract class AssistantTool<TArgs> : AssistantTool
    where TArgs : class
{
    protected abstract string Description { get; }

    public override async Task<AIFunctionDeclaration> GetDeclarationAsync(CancellationToken cancellationToken)
    {
        var schema = AIJsonUtilities.CreateJsonSchema(
            typeof(TArgs),
            serializerOptions: ToolJson.Options,
            inferenceOptions: new AIJsonSchemaCreateOptions
            {
                TransformOptions = new AIJsonSchemaTransformOptions { DisallowAdditionalProperties = true },
            });

        var node = JsonNode.Parse(schema.GetRawText())!.AsObject();
        await CustomizeSchemaAsync(node, cancellationToken);
        return AIFunctionFactory.CreateDeclaration(Name, Description, JsonSerializer.SerializeToElement(node));
    }

    public override async Task<ToolOutcome> InvokeAsync(IDictionary<string, object?>? arguments, ToolContext context, CancellationToken cancellationToken)
    {
        TArgs? parsed;
        try
        {
            // Values are JsonElements from the provider (or plain values in tests): one round trip through JSON binds both.
            var json = JsonSerializer.SerializeToElement(arguments ?? new Dictionary<string, object?>(), ToolJson.Options);
            parsed = json.Deserialize<TArgs>(ToolJson.Options);
        }
        catch (JsonException ex)
        {
            return ToolOutcome.Error($"Invalid arguments for {Name}: {ex.Message}");
        }

        return parsed is null
            ? ToolOutcome.Error($"Invalid arguments for {Name}: an object is required.")
            : await ExecuteAsync(parsed, context, cancellationToken);
    }

    /// <summary>Changes the generated schema, e.g. adds an <c>enum</c> of values known only at run time.</summary>
    protected virtual Task CustomizeSchemaAsync(JsonObject schema, CancellationToken cancellationToken) => Task.CompletedTask;

    protected abstract Task<ToolOutcome> ExecuteAsync(TArgs arguments, ToolContext context, CancellationToken cancellationToken);
}

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
