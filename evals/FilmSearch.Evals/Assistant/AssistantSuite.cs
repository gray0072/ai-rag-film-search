using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using FilmSearch.AI.Assistant;
using FilmSearch.AI.Prompts;
using FilmSearch.AI.Providers;
using FilmSearch.AI.Telemetry;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using FilmSearch.Core.Owners;
using FilmSearch.Evals.Datasets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FilmSearch.Evals.Assistant;

/// <summary>
/// Assistant suite (spec 09 §3): plays the user turns of each case through the real <see cref="IAssistantChat"/> (the
/// same agent loop, tools, prompts and storage as the API, no HTTP) as a new owner, records the events, the cart and
/// orders after each turn and the span data (model calls, iteration limit), runs the deterministic checks and, at the
/// end of the case, the LLM judge. Each turn runs in its own DI scope, like an HTTP request.
/// </summary>
public sealed class AssistantSuite(
    IServiceProvider services,
    IReadOnlyDictionary<Guid, int> tmdbIds,
    SeedCatalog seed,
    AssistantJudge? judge,
    int parallel)
{
    private const string TurnSpan = "invoke_agent assistant";
    private const int JudgeParallel = 2;
    private const int JudgeAttempts = 8;

    private readonly Dictionary<int, Guid> _movieIds = tmdbIds.ToDictionary(p => p.Value, p => p.Key);
    private readonly ConcurrentDictionary<string, (int ModelCalls, bool LimitHit)> _spans = new();

    public async Task<IReadOnlyList<AssistantCaseResult>> RunAsync(IReadOnlyList<AssistantCase> cases, int repeat, CancellationToken cancellationToken)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AiMetrics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == TurnSpan && activity.GetTagItem(AssistantChat.ConversationTag) is string conversation)
                {
                    _spans[conversation] = (
                        activity.GetTagItem("filmsearch.assistant.model_calls") as int? ?? 0,
                        activity.GetTagItem("filmsearch.assistant.iteration_limit_hit") as bool? ?? false);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var jobs = cases.SelectMany(c => Enumerable.Range(1, repeat).Select(r => (Case: c, Repeat: r))).ToList();
        var runs = new ConcurrentDictionary<(string, int), AssistantRun>();
        var done = 0;
        Console.Error.WriteLine($"Assistant: {cases.Count} cases × {repeat}, {parallel} in parallel{(judge is null ? ", no judge" : $", judge {judge.PromptId}")}");

        await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken }, async (job, ct) =>
        {
            var run = await PlayAsync(job.Case, job.Repeat, ct);
            runs[(job.Case.Id, job.Repeat)] = run;
            var failed = run.Turns.SelectMany(t => t.Checks).Where(c => !c.Passed).Select(c => c.Name).Distinct().ToList();
            var number = Interlocked.Increment(ref done);
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[{number}/{jobs.Count}] {job.Case.Id} #{job.Repeat}: {(run.Passed ? "pass" : "FAIL " + string.Join(",", failed))} ({run.WallMs / 1000:0.0} s)"));
        });

        if (judge is not null)
        {
            // The judge runs after the conversations, with few parallel calls: cloud providers limit requests per minute
            // (OpenRouter: 20 per minute and model for new accounts), and a lost score biases the mean.
            Console.Error.WriteLine($"Judge: {jobs.Count} runs");
            done = 0;
            await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = JudgeParallel, CancellationToken = cancellationToken }, async (job, ct) =>
            {
                var run = runs[(job.Case.Id, job.Repeat)];
                var (scores, error) = await JudgeAsync(job.Case, run.Turns, ct);
                runs[(job.Case.Id, job.Repeat)] = run with { Judge = scores, JudgeError = error };
                var number = Interlocked.Increment(ref done);
                if (number % 20 == 0 || number == jobs.Count)
                {
                    Console.Error.WriteLine($"Judge: {number}/{jobs.Count}");
                }
            });
        }

        return [.. cases.Select(c => new AssistantCaseResult(c, [.. Enumerable.Range(1, repeat).Select(r => runs[(c.Id, r)])]))];
    }

    private async Task<AssistantRun> PlayAsync(AssistantCase assistantCase, int repeat, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var owner = Guid.CreateVersion7();
        var locale = assistantCase.Locale;
        Guid conversationId;

        await using (var scope = CreateScope(owner))
        {
            var sp = scope.ServiceProvider;
            foreach (var line in assistantCase.Setup?.Cart ?? [])
            {
                var offer = line.Offer == "rent" ? OfferType.Rent : OfferType.Buy;
                await sp.GetRequiredService<ICartService>().AddItemAsync(_movieIds[line.TmdbId], offer, CartItemSource.User, locale, cancellationToken);
            }

            var store = sp.GetRequiredService<IConversationStore>();
            var conversation = store.Add(locale, sp.GetRequiredService<IOptions<AiOptions>>().Value.ActiveChatProfile);
            var seq = 0;
            foreach (var text in assistantCase.Setup?.StoppedMessages ?? [])
            {
                var now = DateTimeOffset.UtcNow;
                store.AddMessage(new ConversationMessage
                {
                    Id = Guid.CreateVersion7(now),
                    ConversationId = conversation.Id,
                    Seq = ++seq,
                    Role = MessageRole.User,
                    Content = text,
                    CreatedAt = now,
                });
                conversation.Title ??= AssistantChat.Title(text);
            }

            await store.SaveChangesAsync(cancellationToken);
            conversationId = conversation.Id;
        }

        var turns = new List<TurnRecord>();
        foreach (var turn in assistantCase.Turns)
        {
            var record = new TurnRecord { Number = turns.Count + 1, User = turn.User };
            turns.Add(record);
            await PlayTurnAsync(conversationId, owner, locale, record, cancellationToken);
            TurnChecks.Evaluate(locale, turn, record);
        }

        return new AssistantRun(repeat, turns, null, null, stopwatch.Elapsed.TotalMilliseconds);
    }

    /// <summary>One judge call; a rate limit (HTTP 429) is waited out with growing pauses.</summary>
    private async Task<(JudgeScores? Scores, string? Error)> JudgeAsync(AssistantCase assistantCase, IReadOnlyList<TurnRecord> turns, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await judge!.JudgeAsync(assistantCase, turns, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= JudgeAttempts || !ex.Message.Contains("429", StringComparison.Ordinal))
                {
                    return (null, $"the judge failed: {ex.Message.Split('\n')[0].Trim()}");
                }

                await Task.Delay(TimeSpan.FromSeconds(10 * attempt), cancellationToken);
            }
        }
    }

    private async Task PlayTurnAsync(Guid conversationId, Guid owner, string locale, TurnRecord record, CancellationToken cancellationToken)
    {
        await using var scope = CreateScope(owner);
        var sp = scope.ServiceProvider;
        var store = sp.GetRequiredService<IConversationStore>();
        var conversation = await store.FindAsync(conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found.");

        _spans.TryRemove(conversationId.ToString(), out _);
        var started = new Dictionary<string, ToolStartedEvent>(StringComparer.Ordinal);
        await foreach (var item in sp.GetRequiredService<IAssistantChat>().SendAsync(conversation, record.User, locale, cancellationToken))
        {
            switch (item)
            {
                case MessageDeltaEvent delta:
                    record.Answer += delta.Text;
                    break;
                case ToolStartedEvent tool:
                    started[tool.CallId] = tool;
                    break;
                case ToolCompletedEvent tool:
                    record.Tools.Add(new ToolCallRecord(tool.Name, started.GetValueOrDefault(tool.CallId)?.ArgsSummary ?? "", tool.Ok));
                    break;
                case MoviesEvent movies:
                    record.Cards.AddRange(movies.Items.Select(ToCard));
                    break;
                case CartUpdatedEvent:
                    record.CartUpdated = true;
                    break;
                case ConfirmationRequiredEvent confirmation:
                    record.ConfirmationRequired = true;
                    record.ConfirmationTotal = string.Create(CultureInfo.InvariantCulture, $"{confirmation.Summary.TotalMinor / 100m:0.00} {confirmation.Summary.Currency}");
                    break;
                case MessageCompletedEvent completed:
                    record.InputTokens = completed.Usage.InputTokens;
                    record.OutputTokens = completed.Usage.OutputTokens;
                    record.LatencyMs = (int)completed.LatencyMs;
                    break;
                case ErrorEvent error:
                    record.Error = $"{error.Code}: {error.Message}";
                    break;
            }
        }

        if (_spans.TryGetValue(conversationId.ToString(), out var span))
        {
            record.ModelCalls = span.ModelCalls;
            record.IterationLimitHit = span.LimitHit;
        }

        var cart = await sp.GetRequiredService<ICartService>().GetAsync(locale, cancellationToken);
        record.CartAfter.AddRange(cart.Items.Select(i => new CartLineRecord(
            tmdbIds.GetValueOrDefault(i.MovieId), i.Title, i.OfferType == OfferType.Rent ? "rent" : "buy")));
        record.OrdersAfter = (await sp.GetRequiredService<ICheckoutService>().ListOrdersAsync(cancellationToken)).Count;

        await CheckGroundingAsync(sp, store, conversationId, record, cancellationToken);
    }

    /// <summary>
    /// The product's <see cref="GroundingCheck"/> over the answer, with the references the model had: every tool result
    /// and user message of the conversation, the system prompt with the rolling summary.
    /// </summary>
    private static async Task CheckGroundingAsync(IServiceProvider sp, IConversationStore store, Guid conversationId, TurnRecord record, CancellationToken cancellationToken)
    {
        if (record.Answer.Length == 0)
        {
            return;
        }

        var conversation = await store.FindAsync(conversationId, cancellationToken);
        var messages = await store.GetMessagesAsync(conversationId, afterSeq: 0, cancellationToken);
        var settings = sp.GetRequiredService<IOptions<AssistantOptions>>().Value;
        var year = sp.GetRequiredService<TimeProvider>().GetUtcNow().Year.ToString(CultureInfo.InvariantCulture);
        var systemPrompt = sp.GetRequiredService<PromptLibrary>()
            .Get(AssistantChat.PromptName, settings.PromptVersion)
            .Render(new Dictionary<string, string> { ["current_year"] = year });

        var references = messages
            .Where(m => m.Role is MessageRole.Tool or MessageRole.User)
            .Select(m => m.Content)
            .Append(systemPrompt)
            .Append(conversation?.Summary ?? "");
        var result = GroundingCheck.Check(record.Answer, references);
        record.Titles = result.Titles;
        record.UnknownTitles = result.Unknown;
    }

    private CardRecord ToCard(MovieCard card)
    {
        var tmdbId = tmdbIds.GetValueOrDefault(card.Id);
        var genres = seed.Movies.TryGetValue(tmdbId, out var slugs) ? (IReadOnlyList<string>)[.. slugs.Order(StringComparer.Ordinal)] : [];
        return new CardRecord(tmdbId, card.Title, card.Year, genres);
    }

    private AsyncServiceScope CreateScope(Guid owner)
    {
        var scope = services.CreateAsyncScope();
        ((EvalOwnerContext)scope.ServiceProvider.GetRequiredService<IOwnerContext>()).OwnerId = owner;
        return scope;
    }
}
