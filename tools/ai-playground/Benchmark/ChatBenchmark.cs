using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FilmSearch.AI.Providers;
using Microsoft.Extensions.AI;

namespace FilmSearch.AiPlayground.Benchmark;

/// <summary>
/// Small chat model benchmark (I05): tool call correctness on 10 prompts, valid and correct JSON (structured output) on
/// 10 prompts, en/ru/sv free answers for a human judgment of language quality, and generation speed. Prompts are
/// in English, Russian and Swedish. Temperature 0 for the scored parts, so reruns are comparable.
/// </summary>
internal sealed class ChatBenchmark(IChatClient chat, string model, string reasoning, ModelPricing? pricing)
{
    private const int MaxOutputTokens = 2048;

    private long _inputTokens;
    private long _outputTokens;

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"## Chat model `{model}`, reasoning effort {reasoning}").AppendLine();

        var total = Stopwatch.StartNew();
        var (toolScore, toolRows, parallel) = await RunToolCasesAsync(ct);
        var (jsonValid, jsonCorrect, jsonRows) = await RunJsonCasesAsync(ct);
        var (speedRows, answers, tokensPerSecond) = await RunLanguageCasesAsync(ct);

        report.AppendLine("| Metric | Value |").AppendLine("|---|---|");
        report.AppendLine(CultureInfo.InvariantCulture, $"| Tool calls correct | {toolScore}/{ToolCases.Count} |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| Parallel tool calls (2 movies in one message) | {parallel} |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| JSON valid | {jsonValid}/{JsonCases.Count} |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| JSON fields correct | {jsonCorrect}/{JsonCases.Count} |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| Generation speed (mean) | {tokensPerSecond:F1} tokens/s |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| Benchmark time | {total.Elapsed.TotalSeconds:F0} s |");
        report.AppendLine(CultureInfo.InvariantCulture, $"| Tokens in / out | {_inputTokens} / {_outputTokens} |");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"| Estimated cost | {(pricing is null ? "n/a" : "$" + pricing.Estimate(_inputTokens, _outputTokens).ToString("0.#####", CultureInfo.InvariantCulture))} |").AppendLine();

        report.AppendLine("### Tool calls").AppendLine().AppendLine("| # | Prompt | Expected | Got | OK | s |").AppendLine("|---|---|---|---|---|---|");
        report.Append(toolRows).AppendLine();
        report.AppendLine("### JSON output").AppendLine().AppendLine("| # | Prompt | Valid | Correct | Failed checks | s |").AppendLine("|---|---|---|---|---|---|");
        report.Append(jsonRows).AppendLine();
        report.AppendLine("### Speed").AppendLine().AppendLine("| Lang | First text, s | Total, s | Output tokens (incl. reasoning) | Tokens/s |").AppendLine("|---|---|---|---|---|");
        report.Append(speedRows).AppendLine();
        report.AppendLine("### Answers (language quality)").AppendLine().Append(answers);

        return report.ToString();
    }

    private async Task<(int Score, string Rows, string Parallel)> RunToolCasesAsync(CancellationToken ct)
    {
        var rows = new StringBuilder();
        var score = 0;
        var options = new ChatOptions { Tools = FakeCatalogTools.All, ToolMode = ChatToolMode.Auto, Temperature = 0, MaxOutputTokens = MaxOutputTokens };

        for (var i = 0; i < ToolCases.Count; i++)
        {
            var testCase = ToolCases[i];
            var (calls, seconds, error) = await CallToolsAsync(testCase.Prompt, options, ct);
            var ok = error is null && testCase.Check(calls);
            score += ok ? 1 : 0;
            var got = error ?? (calls.Count == 0 ? "(text)" : string.Join("; ", calls.Select(Describe)));
            rows.AppendLine(CultureInfo.InvariantCulture,
                $"| {i + 1} | {Cell(testCase.Prompt)} | {testCase.Expected} | {Cell(got)} | {(ok ? "✅" : "❌")} | {seconds:F1} |");
        }

        // Not scored: does the model return two calls in one response? Needed to know for the agent loop (I12).
        var (parallelCalls, _, parallelError) = await CallToolsAsync("Add movies 3 and 4 to my cart, I want to buy both.", options, ct);
        var parallel = parallelError
            ?? $"{parallelCalls.Count(c => c.Name == "add_to_cart")} add_to_cart call(s) in one response";

        return (score, rows.ToString(), parallel);
    }

    private async Task<(List<FunctionCallContent> Calls, double Seconds, string? Error)> CallToolsAsync(
        string prompt, ChatOptions options, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await chat.GetResponseAsync(
                [new(ChatRole.System, Demos.ToolSystemPrompt), new(ChatRole.User, prompt)], options, ct);
            Count(response.Usage);
            var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
            return (calls, stopwatch.Elapsed.TotalSeconds, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ([], stopwatch.Elapsed.TotalSeconds, $"error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<(int Valid, int Correct, string Rows)> RunJsonCasesAsync(CancellationToken ct)
    {
        var rows = new StringBuilder();
        int valid = 0, correct = 0;

        for (var i = 0; i < JsonCases.Count; i++)
        {
            var testCase = JsonCases[i];
            var stopwatch = Stopwatch.StartNew();
            SearchIntent? intent = null;
            string failed;
            try
            {
                var response = await chat.GetResponseAsync<SearchIntent>(
                    [new(ChatRole.System, "Extract the search request of a movie store customer."), new(ChatRole.User, testCase.Prompt)],
                    new ChatOptions { Temperature = 0, MaxOutputTokens = MaxOutputTokens },
                    useJsonSchemaResponseFormat: true,
                    cancellationToken: ct);
                Count(response.Usage);
                failed = response.TryGetResult(out intent) ? "" : $"invalid: {Cell(response.Text, 80)}";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed = $"error: {ex.GetType().Name}";
            }

            if (intent is not null)
            {
                valid++;
                var failedChecks = testCase.Checks.Where(c => !c.Check(intent)).Select(c => c.Name).ToList();
                failed = string.Join(", ", failedChecks);
                correct += failedChecks.Count == 0 ? 1 : 0;
            }

            rows.AppendLine(CultureInfo.InvariantCulture,
                $"| {i + 1} | {Cell(testCase.Prompt)} | {(intent is null ? "❌" : "✅")} | {(intent is not null && failed.Length == 0 ? "✅" : "❌")} | {Cell(failed)} | {stopwatch.Elapsed.TotalSeconds:F1} |");
        }

        return (valid, correct, rows.ToString());
    }

    private async Task<(string SpeedRows, string Answers, double TokensPerSecond)> RunLanguageCasesAsync(CancellationToken ct)
    {
        (string Lang, string Prompt)[] prompts =
        [
            ("en", "Recommend one movie for a cozy evening and explain the choice in two or three sentences."),
            ("ru", "Посоветуй один фильм для уютного вечера и объясни выбор в двух-трёх предложениях."),
            ("sv", "Rekommendera en film för en mysig kväll och förklara valet med två eller tre meningar."),
        ];

        var speedRows = new StringBuilder();
        var answers = new StringBuilder();
        var speeds = new List<double>();

        foreach (var (lang, prompt) in prompts)
        {
            var stopwatch = Stopwatch.StartNew();
            TimeSpan? firstChunk = null, firstText = null;
            var updates = new List<ChatResponseUpdate>();
            try
            {
                await foreach (var update in chat.GetStreamingResponseAsync(
                    prompt, new ChatOptions { Temperature = 0.3f, MaxOutputTokens = MaxOutputTokens }, ct))
                {
                    // Hidden reasoning (LM Studio reasoning_content) arrives as chunks without text: output tokens are
                    // counted from the first chunk, while the user waits until the first text.
                    firstChunk ??= stopwatch.Elapsed;
                    if (update.Text.Length > 0)
                    {
                        firstText ??= stopwatch.Elapsed;
                    }

                    updates.Add(update);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                updates.Add(new ChatResponseUpdate(ChatRole.Assistant, $"error: {ex.GetType().Name}: {ex.Message}"));
            }

            var response = updates.ToChatResponse();
            Count(response.Usage);
            var output = response.Usage?.OutputTokenCount ?? 0;
            var generationSeconds = Math.Max((stopwatch.Elapsed - (firstChunk ?? TimeSpan.Zero)).TotalSeconds, 0.001);
            var speed = output / generationSeconds;
            speeds.Add(speed);

            speedRows.AppendLine(CultureInfo.InvariantCulture,
                $"| {lang} | {firstText?.TotalSeconds:F2} | {stopwatch.Elapsed.TotalSeconds:F2} | {output} | {speed:F1} |");
            answers.AppendLine(CultureInfo.InvariantCulture, $"**{lang}:** {prompt}").AppendLine();
            answers.AppendLine(string.Join('\n', response.Text.Trim().Split('\n').Select(line => "> " + line))).AppendLine();
        }

        return (speedRows.ToString(), answers.ToString(), speeds.Average());
    }

    private void Count(UsageDetails? usage)
    {
        _inputTokens += usage?.InputTokenCount ?? 0;
        _outputTokens += usage?.OutputTokenCount ?? 0;
    }

    private static string Describe(FunctionCallContent call) =>
        $"{call.Name}({string.Join(", ", (call.Arguments ?? new Dictionary<string, object?>()).Select(a => $"{a.Key}={JsonSerializer.Serialize(a.Value)}"))})";

    private static string Cell(string text, int max = 120)
    {
        var single = text.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal);
        return single.Length <= max ? single : single[..max] + "…";
    }

    // ----- Tool cases -----

    private sealed record ToolCase(string Prompt, string Expected, Func<IReadOnlyList<FunctionCallContent>, bool> Check);

    private static readonly IReadOnlyList<ToolCase> ToolCases =
    [
        new("Find me some sci-fi movies from the 90s", "search_movies yearFrom=1990 yearTo=1999",
            c => One(c, "search_movies", a => Int(a, "yearFrom") is 1990 && Int(a, "yearTo") is 1999 or 2000)),
        new("Найди комедии дешевле 5 долларов", "search_movies maxPrice=5",
            c => One(c, "search_movies", a => Dec(a, "maxPrice") is > 0 and <= 5)),
        new("Visa detaljer om filmen med id 42", "get_movie_details movieId=42",
            c => One(c, "get_movie_details", a => Int(a, "movieId") == 42)),
        new("Add movie 17 to my cart, I want to rent it", "add_to_cart 17 rent",
            c => One(c, "add_to_cart", a => Int(a, "movieId") == 17 && Str(a, "offerType") == "rent")),
        new("Что у меня в корзине?", "view_cart",
            c => One(c, "view_cart", _ => true)),
        new("Lägg till film 8 i kundvagnen, jag vill köpa den", "add_to_cart 8 buy",
            c => One(c, "add_to_cart", a => Int(a, "movieId") == 8 && Str(a, "offerType") == "buy")),
        new("Hi! What can you help me with?", "no tool",
            c => c.Count == 0),
        new("Посоветуй что-нибудь похожее на Интерстеллар", "search_movies",
            c => One(c, "search_movies", a => !string.IsNullOrWhiteSpace(Str(a, "query")))),
        new("Remove movie 5 from my cart", "remove_from_cart 5",
            c => One(c, "remove_from_cart", a => Int(a, "movieId") == 5)),
        new("Jag vill se en skräckfilm från 2010-talet", "search_movies yearFrom=2010 yearTo=2019",
            c => One(c, "search_movies", a => Int(a, "yearFrom") == 2010 && Int(a, "yearTo") is 2019 or 2020)),
    ];

    private static bool One(IReadOnlyList<FunctionCallContent> calls, string name, Func<IDictionary<string, object?>, bool> check) =>
        calls.Count == 1 && calls[0].Name == name && check(calls[0].Arguments ?? new Dictionary<string, object?>());

    private static JsonElement? Arg(IDictionary<string, object?> args, string name) =>
        args.TryGetValue(name, out var value) && value is not null ? JsonSerializer.SerializeToElement(value) : null;

    private static int? Int(IDictionary<string, object?> args, string name) => Arg(args, name) switch
    {
        { ValueKind: JsonValueKind.Number } e when e.TryGetInt32(out var n) => n,
        { ValueKind: JsonValueKind.String } e when int.TryParse(e.GetString(), CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    private static decimal? Dec(IDictionary<string, object?> args, string name) => Arg(args, name) switch
    {
        { ValueKind: JsonValueKind.Number } e => e.GetDecimal(),
        { ValueKind: JsonValueKind.String } e when decimal.TryParse(e.GetString(), CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    private static string? Str(IDictionary<string, object?> args, string name) =>
        Arg(args, name) is { ValueKind: JsonValueKind.String } e ? e.GetString()?.ToLowerInvariant() : null;

    // ----- JSON cases -----

    private sealed record JsonCheck(string Name, Func<SearchIntent, bool> Check);

    private sealed record JsonCase(string Prompt, params JsonCheck[] Checks);

    private static JsonCheck Lang(IntentLanguage lang) => new($"language={lang}", i => i.Language == lang);

    private static JsonCheck Years(int? from, int? to) => new($"years={from}-{to}", i => i.YearFrom == from && i.YearTo == to);

    private static JsonCheck From(int from) => new($"yearFrom={from}", i => i.YearFrom == from);

    private static JsonCheck Price(decimal? max) => new($"maxPrice={max}", i => i.MaxPriceUsd == max);

    private static JsonCheck Genre(params IntentGenre[] anyOf) => new($"genre∈{string.Join('/', anyOf)}",
        i => i.Genres.Any(anyOf.Contains));

    private static readonly IReadOnlyList<JsonCase> JsonCases =
    [
        new("Funny movies from the 80s under $5", Genre(IntentGenre.Comedy), Years(1980, 1989), Price(5), Lang(IntentLanguage.En)),
        new("Хочу посмотреть фантастику, снятую после 2010 года", Genre(IntentGenre.ScienceFiction), Lang(IntentLanguage.Ru)),
        new("En romantisk film från 90-talet, max 3 dollar", Genre(IntentGenre.Romance), Years(1990, 1999), Price(3), Lang(IntentLanguage.Sv)),
        new("Something like Inception but less confusing", Years(null, null), Price(null), Lang(IntentLanguage.En)),
        new("Мультфильм для детей не дороже 4 долларов", Genre(IntentGenre.Animation, IntentGenre.Family), Price(4), Lang(IntentLanguage.Ru)),
        new("Skräckfilmer från 1970-talet", Genre(IntentGenre.Horror), Years(1970, 1979), Lang(IntentLanguage.Sv)),
        new("A war drama released between 1995 and 2005", Genre(IntentGenre.War, IntentGenre.Drama), Years(1995, 2005), Lang(IntentLanguage.En)),
        new("Криминальный триллер не старше 2000 года", Genre(IntentGenre.Crime, IntentGenre.Thriller), From(2000), Lang(IntentLanguage.Ru)),
        new("Något roligt att se med familjen", Genre(IntentGenre.Comedy, IntentGenre.Family), Price(null), Lang(IntentLanguage.Sv)),
        new("Documentaries about space, cheaper than 2 dollars", Genre(IntentGenre.Documentary), Price(2), Lang(IntentLanguage.En)),
    ];
}
