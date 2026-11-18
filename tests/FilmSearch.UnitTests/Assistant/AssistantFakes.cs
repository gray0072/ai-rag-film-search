using FilmSearch.AI.Assistant;
using FilmSearch.AI.Assistant.Tools;
using FilmSearch.AI.Prompts;
using FilmSearch.AI.Providers;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FilmSearch.UnitTests.Assistant;

/// <summary>Builds <see cref="AssistantChat"/> with fakes: a scripted model, an in-memory store, a fixed search result.</summary>
internal sealed class AssistantHarness
{
    public const string Model = "test-model";

    public ScriptedStreamingChatClient Chat { get; } = new();

    public InMemoryConversationStore Store { get; } = new();

    public FakeMovieSearch Search { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));

    public ConversationTurnLocks Locks { get; } = new();

    public AssistantOptions Options { get; } = new();

    public AiOptions Ai { get; } = new()
    {
        ActiveChatProfile = "local",
        ChatProfiles = new(StringComparer.OrdinalIgnoreCase) { ["local"] = new ChatProfileOptions { Endpoint = "http://localhost:1234/v1", Model = Model, ApiKey = "x" } },
        Pricing = new(StringComparer.OrdinalIgnoreCase) { [Model] = new ModelPricing { InputPerMTokUsd = 1, OutputPerMTokUsd = 10 } },
    };

    public AssistantChat Create()
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options);
        var tool = new SearchMoviesTool(Search, new GenreCatalog(), new MemoryCache(new MemoryCacheOptions()), options);
        return new AssistantChat(
            Store,
            Chat,
            tool,
            new ConversationSummarizer(new PromptLibrary(), options, NullLogger<ConversationSummarizer>.Instance),
            new PromptLibrary(),
            Locks,
            Time,
            options,
            Microsoft.Extensions.Options.Options.Create(Ai),
            NullLogger<AssistantChat>.Instance);
    }

    public async Task<List<AssistantEvent>> SendAsync(Conversation conversation, string text, string locale = "en")
    {
        var events = new List<AssistantEvent>();
        await foreach (var item in Create().SendAsync(conversation, text, locale, TestContext.Current.CancellationToken))
        {
            events.Add(item);
        }

        return events;
    }

    public static MovieSummary Movie(string title, int year = 2014, string? overview = null) => new(
        Guid.CreateVersion7(),
        title,
        title,
        year,
        120,
        overview ?? $"{title} is a movie about space.",
        "/poster.jpg",
        7.84m,
        1000,
        [new GenreItem(1, "science-fiction", "Science Fiction")],
        [new OfferItem(Guid.CreateVersion7(), OfferType.Buy, 999, "USD", null), new OfferItem(Guid.CreateVersion7(), OfferType.Rent, 399, "USD", 48)]);
}

/// <summary>
/// A model that streams scripted answers: one script per streaming call, in order (the last one repeats). Each content
/// becomes one update; every call reports 100 input and 10 output tokens. Non-streaming calls (the summary) answer with
/// <see cref="SummaryAnswer"/>. Records every request.
/// </summary>
internal sealed class ScriptedStreamingChatClient : IChatClient
{
    private readonly Queue<Func<IEnumerable<AIContent>>> _scripts = new();
    private Func<IEnumerable<AIContent>>? _last;

    public List<(List<ChatMessage> Messages, ChatOptions? Options)> StreamRequests { get; } = [];

    public List<List<ChatMessage>> Requests { get; } = [];

    public string SummaryAnswer { get; set; } = "The user wants space movies.";

    public void Answer(params AIContent[] contents) => _scripts.Enqueue(() => contents);

    public void Fail(Exception exception) => _scripts.Enqueue(() => throw exception);

    public static TextContent Text(string text) => new(text);

    public static FunctionCallContent Call(string callId, string name, Dictionary<string, object?> arguments) => new(callId, name, arguments);

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests.Add([.. messages]);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, SummaryAnswer))
        {
            Usage = new UsageDetails { InputTokenCount = 50, OutputTokenCount = 5 },
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StreamRequests.Add(([.. messages], options));
        var script = _scripts.Count > 0 ? _scripts.Dequeue() : _last ?? throw new InvalidOperationException("No script for this call.");
        _last = script;

        foreach (var content in script())
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [content]) { ModelId = AssistantHarness.Model };
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 })]);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class InMemoryConversationStore : IConversationStore
{
    public Guid OwnerId { get; } = Guid.NewGuid();

    public List<Conversation> Conversations { get; } = [];

    public List<ConversationMessage> Messages { get; } = [];

    public Dictionary<Guid, MessageFeedback> Feedback { get; } = [];

    public int Saves { get; private set; }

    public Conversation Add(string locale, string profileName)
    {
        var conversation = new Conversation { Id = Guid.CreateVersion7(), OwnerId = OwnerId, Locale = locale, ProfileName = profileName };
        Conversations.Add(conversation);
        return conversation;
    }

    public Task<IReadOnlyList<Conversation>> ListAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Conversation>>([.. Conversations.OrderByDescending(c => c.UpdatedAt).Take(limit)]);

    public Task<Conversation?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Conversations.SingleOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(Guid conversationId, int afterSeq, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConversationMessage>>([.. Messages.Where(m => m.ConversationId == conversationId && m.Seq > afterSeq).OrderBy(m => m.Seq)]);

    public void AddMessage(ConversationMessage message) => Messages.Add(message);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<Guid, MessageFeedback>> GetFeedbackAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, MessageFeedback>>(Feedback.Where(f => messageIds.Contains(f.Key)).ToDictionary());

    public Task<FeedbackResult> SaveFeedbackAsync(Guid messageId, int rating, string? comment, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>Returns <see cref="Results"/> for every query and records the queries.</summary>
internal sealed class FakeMovieSearch : IMovieSearch
{
    public List<MovieSummary> Results { get; set; } = [];

    public List<SearchQuery> Queries { get; } = [];

    public bool Supports(SearchMode mode) => true;

    public Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        var items = Results.Take(query.PageSize).Select((m, i) => new SearchItem(m, 1.0 / (i + 1), null)).ToList();
        return Task.FromResult(new SearchResult(items, query.Page, query.PageSize, Results.Count));
    }

    public Task<IReadOnlyList<SearchItem>?> FindSimilarAsync(Guid movieId, MovieFilter filter, string locale, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>A catalog that only knows three genres.</summary>
internal sealed class GenreCatalog : ICatalogReader
{
    public Task<IReadOnlyList<GenreItem>> ListGenresAsync(string locale, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GenreItem>>([new(1, "science-fiction", "Science Fiction"), new(2, "horror", "Horror"), new(3, "comedy", "Comedy")]);

    public Task<PagedResult<MovieSummary>> ListMoviesAsync(MovieListQuery query, string locale, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<MovieSummary>> GetMoviesAsync(IReadOnlyList<Guid> ids, string locale, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<MovieDetails?> GetMovieAsync(Guid id, string locale, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<MovieDetails>> GetMovieDetailsAsync(IReadOnlyList<Guid> ids, string locale, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Guid>> GetMovieIdsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> HasMoviesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
