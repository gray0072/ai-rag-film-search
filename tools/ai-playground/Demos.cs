using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FilmSearch.AI.Providers;
using FilmSearch.AI.Telemetry;
using Microsoft.Extensions.AI;

namespace FilmSearch.AiPlayground;

internal static class Demos
{
    private static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public const string ToolSystemPrompt =
        "You are the assistant of an online movie store. Use the tools to search the catalog and to manage the cart. "
        + "Movie ids are integers. If the user only greets you or asks something you can answer without data, answer without tools. "
        + "Answer in the language of the user.";

    public static async Task ChatAsync(IChatClient chat, string text, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await chat.GetResponseAsync(text, cancellationToken: ct);

        Console.WriteLine(response.Text);
        PrintUsage(response, stopwatch.Elapsed);
    }

    public static async Task StreamAsync(IChatClient chat, string text, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        TimeSpan? firstToken = null;
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in chat.GetStreamingResponseAsync(text, cancellationToken: ct))
        {
            if (update.Text.Length > 0)
            {
                firstToken ??= stopwatch.Elapsed;
                Console.Write(update.Text);
            }

            updates.Add(update);
        }

        Console.WriteLine();
        var response = updates.ToChatResponse();
        var output = response.Usage?.OutputTokenCount ?? 0;
        var generation = stopwatch.Elapsed - (firstToken ?? TimeSpan.Zero);
        Console.Error.WriteLine(
            $"--- {updates.Count} chunks, first token after {firstToken?.TotalSeconds:F2} s, "
            + $"{output} output tokens, {output / Math.Max(generation.TotalSeconds, 0.001):F1} tokens/s");
    }

    /// <summary>
    /// The tool loop written by hand (ADR-0006: own loop first): send tools → the model answers with function calls →
    /// run them → send the results back → repeat until the model answers with text.
    /// </summary>
    public static async Task ToolsAsync(IChatClient chat, string text, CancellationToken ct)
    {
        List<ChatMessage> history = [new(ChatRole.System, ToolSystemPrompt), new(ChatRole.User, text)];
        var options = new ChatOptions { Tools = FakeCatalogTools.All, ToolMode = ChatToolMode.Auto };
        var functions = FakeCatalogTools.All.OfType<AIFunction>().ToDictionary(f => f.Name);

        for (var iteration = 1; iteration <= 6; iteration++)
        {
            var response = await chat.GetResponseAsync(history, options, ct);
            history.AddMessages(response);

            var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
            if (calls.Count == 0)
            {
                Console.WriteLine(response.Text);
                return;
            }

            Console.Error.WriteLine($"--- iteration {iteration}: {calls.Count} tool call(s)");
            var results = new List<AIContent>();
            foreach (var call in calls)
            {
                Console.Error.WriteLine($"    {call.Name}({JsonSerializer.Serialize(call.Arguments)})");
                object? result;
                try
                {
                    result = functions.TryGetValue(call.Name, out var function)
                        ? await function.InvokeAsync(new AIFunctionArguments(call.Arguments), ct)
                        : new { error = $"Unknown tool {call.Name}" };
                }
                catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
                {
                    result = new { error = ex.Message };
                }

                Console.Error.WriteLine($"    → {JsonSerializer.Serialize(result)}");
                results.Add(new FunctionResultContent(call.CallId, result));
            }

            history.Add(new ChatMessage(ChatRole.Tool, results));
        }

        Console.Error.WriteLine("--- stopped after 6 iterations");
    }

    public static async Task JsonAsync(IChatClient chat, string text, CancellationToken ct)
    {
        var response = await chat.GetResponseAsync<SearchIntent>(
            [new(ChatRole.System, "Extract the search request of a movie store customer."), new(ChatRole.User, text)],
            new ChatOptions { Temperature = 0 },
            useJsonSchemaResponseFormat: true,
            cancellationToken: ct);

        if (response.TryGetResult(out var intent))
        {
            Console.WriteLine(JsonSerializer.Serialize(intent, Indented));
        }
        else
        {
            Console.WriteLine($"Invalid JSON from the model:\n{response.Text}");
        }
    }

    public static async Task EmbedAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator, EmbeddingProfileOptions profile, CancellationToken ct)
    {
        string[] phrases =
        [
            "a movie about a mafia family",
            "фильм про мафиозную семью",
            "en film om en maffiafamilj",
            "a cartoon for small children",
            "мультфильм для маленьких детей",
            "en tecknad film för små barn",
        ];

        var vectors = await generator.GenerateAsync(phrases.Select(profile.FormatQuery), cancellationToken: ct);
        Console.WriteLine($"{vectors.Count} vectors × {vectors[0].Vector.Length} dimensions\n");

        for (var i = 0; i < phrases.Length; i++)
        {
            var row = string.Join("  ", vectors.Select(v => Similarity.Cosine(vectors[i].Vector.Span, v.Vector.Span).ToString("F2", CultureInfo.InvariantCulture)));
            Console.WriteLine($"{row}   {phrases[i]}");
        }
    }

    private static void PrintUsage(ChatResponse response, TimeSpan elapsed)
    {
        var usage = response.AdditionalProperties?.GetValueOrDefault(AiUsage.PropertyKey) as AiUsage;
        Console.Error.WriteLine(
            $"--- model {response.ModelId}, {usage?.InputTokens} input + {usage?.OutputTokens} output tokens, "
            + $"cost {usage?.EstimatedCostUsd?.ToString("0.######", CultureInfo.InvariantCulture) ?? "n/a"} USD, {elapsed.TotalSeconds:F2} s, finish: {response.FinishReason}");
    }
}

internal static class Similarity
{
    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
