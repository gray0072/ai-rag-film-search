using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FilmSearch.AI.Caching;
using FilmSearch.AI.Prompts;
using FilmSearch.AI.Telemetry;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Localization;
using FilmSearch.Core.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FilmSearch.AI.QueryUnderstanding;

/// <summary>
/// <see cref="IQueryUnderstanding"/> with an LLM and structured output (spec 05 §5):
/// <list type="number">
/// <item>system prompt <c>query-understanding.vN</c> with the genre slugs of the catalog; the user's query as the user
/// message (it is untrusted text, so it never goes into the instructions);</item>
/// <item><c>response_format</c> = JSON Schema generated from <see cref="QueryUnderstandingOutput"/>, genres as an enum;</item>
/// <item>the answer is validated (<see cref="QueryUnderstandingValidator"/>); if invalid, the model gets its answer and
/// the error back and one more try;</item>
/// <item>still invalid, or the provider is down: fallback — no filters, the whole query as semantic text.</item>
/// </list>
/// Valid results are cached per normalized query, prompt version and profile (<see cref="QueryUnderstandingCache"/>, bounded),
/// so repeated queries cost nothing.
/// </summary>
internal sealed partial class QueryUnderstandingService(
    IServiceProvider services,
    PromptLibrary prompts,
    ICatalogReader catalog,
    IMemoryCache cache,
    QueryUnderstandingCache results,
    TimeProvider timeProvider,
    IOptions<QueryUnderstandingOptions> options,
    ILogger<QueryUnderstandingService> logger) : IQueryUnderstanding
{
    public const string PromptName = "query-understanding";

    /// <summary>Span tags: which prompt version produced a result is visible in every trace.</summary>
    public const string PromptTag = "filmsearch.prompt.id";
    public const string SourceTag = "filmsearch.qu.source";
    public const string AttemptsTag = "filmsearch.qu.attempts";

    private const int MaxAttempts = 2;

    private static readonly ActivitySource Activities = new(AiMetrics.SourceName);

    public async Task<UnderstoodQuery> UnderstandAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var settings = options.Value;
        var prompt = prompts.Get(PromptName, settings.PromptVersion);
        using var activity = Activities.StartActivity("query_understanding");
        activity?.SetTag(PromptTag, prompt.Id);

        var cacheKey = $"qu:{prompt.Id}:{settings.ChatProfile}:{Normalize(query)}";
        if (results.TryGet(cacheKey, out var cached) && cached is not null)
        {
            activity?.SetTag(SourceTag, "cache");
            return cached with { Source = QueryUnderstandingSource.Cache };
        }

        var context = await GetContextAsync(prompt, cancellationToken);
        var chat = settings.ChatProfile is { Length: > 0 } profile
            ? services.GetRequiredKeyedService<IChatClient>(profile)
            : services.GetRequiredService<IChatClient>();
        var chatOptions = new ChatOptions
        {
            // Extraction, not creativity: the same query should give the same filters.
            Temperature = 0,
            MaxOutputTokens = settings.MaxOutputTokens,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(context.Schema, QueryUnderstandingSchema.Name, "Filters and search text of a movie search query."),
        };
        List<ChatMessage> messages = [new(ChatRole.System, context.SystemPrompt), new(ChatRole.User, query)];

        var error = "";
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            activity?.SetTag(AttemptsTag, attempt);
            string answer;
            try
            {
                var response = await chat.GetResponseAsync(messages, chatOptions, cancellationToken);
                answer = response.Text;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The provider is down, timed out or failed: no point in a second call.
                LogCallFailed(logger, prompt.Id, ex);
                return Fallback(activity, query, prompt, $"The chat provider failed: {ex.Message}");
            }

            if (QueryUnderstandingValidator.TryParse(answer, context.GenreSlugs, out var understood, out error))
            {
                var result = understood! with { PromptVersion = prompt.Id };
                results.Set(cacheKey, result);
                activity?.SetTag(SourceTag, "llm");
                if (logger.IsEnabled(LogLevel.Information))
                {
                    var json = JsonSerializer.Serialize(result, QueryUnderstandingSchema.JsonOptions);
                    LogUnderstood(logger, prompt.Id, attempt, json);
                }
                return result;
            }

            LogInvalidOutput(logger, prompt.Id, attempt, error);
            messages.Add(new ChatMessage(ChatRole.Assistant, answer));
            messages.Add(new ChatMessage(
                ChatRole.User,
                $"Your answer is invalid: {error} Answer again with only the corrected JSON object for the same query."));
        }

        return Fallback(activity, query, prompt, error);
    }

    /// <summary>Cache key part: case and whitespace do not change the meaning of a query.</summary>
    internal static string Normalize(string query) => QueryText.Normalize(query);

    private static UnderstoodQuery Fallback(Activity? activity, string query, PromptTemplate prompt, string error)
    {
        activity?.SetTag(SourceTag, "fallback");
        activity?.SetStatus(ActivityStatusCode.Error, error);
        return UnderstoodQuery.Fallback(query, prompt.Id, error);
    }

    /// <summary>System prompt and schema with the catalog's genres; cached, since genres change rarely.</summary>
    private async Task<PromptContext> GetContextAsync(PromptTemplate prompt, CancellationToken cancellationToken)
    {
        var year = timeProvider.GetUtcNow().Year;
        var key = $"qu-context:{prompt.Id}:{year}";
        if (cache.TryGetValue(key, out PromptContext? context) && context is not null)
        {
            return context;
        }

        var names = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var locale in Locales.All)
        {
            foreach (var genre in await catalog.ListGenresAsync(locale, cancellationToken))
            {
                if (!names.TryGetValue(genre.Slug, out var list))
                {
                    list = [];
                    names[genre.Slug] = list;
                }

                list.Add(genre.Name);
            }
        }

        var slugs = names.Keys.Order(StringComparer.Ordinal).ToList();
        var genreLines = string.Join("\n", slugs.Select(s => $"- {s}: {string.Join(" / ", names[s].Distinct())}"));
        context = new PromptContext(
            prompt.Render(new Dictionary<string, string>
            {
                ["genres"] = genreLines,
                ["current_year"] = year.ToString(CultureInfo.InvariantCulture),
            }),
            QueryUnderstandingSchema.Create(slugs),
            slugs.ToHashSet(StringComparer.Ordinal));

        cache.Set(key, context, TimeSpan.FromMinutes(options.Value.CacheMinutes));
        return context;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Query understood with prompt {PromptId} after {Attempts} attempt(s): {Result}")]
    private static partial void LogUnderstood(ILogger logger, string promptId, int attempts, string result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Query understanding with prompt {PromptId}: invalid output on attempt {Attempt}: {Error}")]
    private static partial void LogInvalidOutput(ILogger logger, string promptId, int attempt, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Query understanding with prompt {PromptId} failed; falling back to the plain query")]
    private static partial void LogCallFailed(ILogger logger, string promptId, Exception exception);

    private sealed record PromptContext(string SystemPrompt, JsonElement Schema, IReadOnlySet<string> GenreSlugs);
}
