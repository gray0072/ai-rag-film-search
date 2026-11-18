using FilmSearch.Core.Catalog;

namespace FilmSearch.Core.Assistant;

// Contracts of the Assistant module (spec 02 §5, spec 06). Conversations are always the current owner's
// (IOwnerContext): another owner's conversation is "not found".

/// <summary>Storage of conversations and messages of the current owner (unit of work: changes are saved by <see cref="SaveChangesAsync"/>).</summary>
public interface IConversationStore
{
    /// <summary>Adds a new conversation of the owner (saved by <see cref="SaveChangesAsync"/>).</summary>
    Conversation Add(string locale, string profileName);

    /// <summary>The owner's conversations, most recently updated first.</summary>
    Task<IReadOnlyList<Conversation>> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>The owner's conversation (tracked: changes are saved by <see cref="SaveChangesAsync"/>), or null.</summary>
    Task<Conversation?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Messages with <c>Seq &gt; afterSeq</c> in order.</summary>
    Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(Guid conversationId, int afterSeq, CancellationToken cancellationToken);

    void AddMessage(ConversationMessage message);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Feedback on the given messages, by message id.</summary>
    Task<IReadOnlyDictionary<Guid, MessageFeedback>> GetFeedbackAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken);

    /// <summary>Saves or replaces the feedback on an answer of the owner.</summary>
    Task<FeedbackResult> SaveFeedbackAsync(Guid messageId, int rating, string? comment, CancellationToken cancellationToken);
}

public enum FeedbackResult
{
    Saved,

    /// <summary>No such message, or it belongs to another owner.</summary>
    MessageNotFound,

    /// <summary>Only answers of the assistant can be rated.</summary>
    NotAnAnswer,
}

/// <summary>One turn of the assistant: the agent loop over the conversation, streamed as events (spec 04 "SSE events").</summary>
public interface IAssistantChat
{
    /// <summary>
    /// Stores the user's message, runs the agent loop and stores the answer. Never throws for model, tool or provider
    /// failures: the stream then ends with an <see cref="ErrorEvent"/>. Cancelling <paramref name="cancellationToken"/>
    /// (client disconnect) cancels the model call.
    /// </summary>
    /// <param name="conversation">A conversation of the current owner (from <see cref="IConversationStore.FindAsync"/>).</param>
    /// <param name="text">The user's message (already validated: 1–<see cref="AssistantLimits.MaxMessageLength"/> characters).</param>
    /// <param name="locale">Locale of movie texts in tool results and cards.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    IAsyncEnumerable<AssistantEvent> SendAsync(Conversation conversation, string text, string locale, CancellationToken cancellationToken);
}

public static class AssistantLimits
{
    /// <summary>Longest user message (NFR-7: input length limits).</summary>
    public const int MaxMessageLength = 2000;

    public const int MaxFeedbackCommentLength = 1000;

    public const int MaxTitleLength = 80;

    /// <summary>Conversations returned by the list endpoint.</summary>
    public const int ConversationListLimit = 50;
}

// Doc comments of types in the OpenAPI document stay on one line (see SearchModels.cs).

/// <summary>A movie shown next to an assistant message. Built from the catalog (prices from the DB), never from model text (FR-AST-3).</summary>
/// <param name="Id">Movie id.</param>
/// <param name="Title">Title in the request locale.</param>
/// <param name="Year">Release year.</param>
/// <param name="PosterPath">TMDB poster path; the UI builds the image URL.</param>
/// <param name="Genres">Genre names in the request locale.</param>
/// <param name="Rating">Average user rating, 0–10.</param>
/// <param name="Offers">Active offers with current prices.</param>
/// <param name="Reason">Why the movie fits the request (reserved; not filled yet).</param>
public sealed record MovieCard(
    Guid Id,
    string Title,
    int? Year,
    string? PosterPath,
    IReadOnlyList<string> Genres,
    decimal Rating,
    IReadOnlyList<OfferItem> Offers,
    string? Reason = null)
{
    public static MovieCard From(MovieSummary movie) => new(
        movie.Id,
        movie.Title,
        movie.Year,
        movie.PosterPath,
        [.. movie.Genres.Select(g => g.Name)],
        movie.VoteAverage,
        movie.Offers);
}

/// <summary>A conversation in the list.</summary>
/// <param name="Id">Conversation id.</param>
/// <param name="Title">Beginning of the first user message; null before it.</param>
/// <param name="Locale">Locale given at creation.</param>
/// <param name="CreatedAt">Creation time.</param>
/// <param name="UpdatedAt">Time of the last message.</param>
public sealed record ConversationListItem(Guid Id, string? Title, string Locale, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static ConversationListItem From(Conversation conversation) =>
        new(conversation.Id, conversation.Title, conversation.Locale, conversation.CreatedAt, conversation.UpdatedAt);
}

/// <summary>A conversation with its visible messages (user messages and answers; tool rounds are left out).</summary>
/// <param name="Id">Conversation id.</param>
/// <param name="Title">Beginning of the first user message.</param>
/// <param name="Locale">Locale given at creation.</param>
/// <param name="CreatedAt">Creation time.</param>
/// <param name="UpdatedAt">Time of the last message.</param>
/// <param name="Usage">Tokens and estimated cost of the whole conversation (answers and summaries).</param>
/// <param name="Messages">Messages in order.</param>
public sealed record ConversationView(
    Guid Id,
    string? Title,
    string Locale,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    UsageView Usage,
    IReadOnlyList<MessageView> Messages);

/// <summary>A user message or an answer of the assistant.</summary>
/// <param name="Id">Message id (used for feedback).</param>
/// <param name="Role">user or assistant.</param>
/// <param name="Text">Message text (assistant text is markdown).</param>
/// <param name="CreatedAt">Creation time.</param>
/// <param name="Movies">Movie cards of an answer, built from the catalog now (current prices).</param>
/// <param name="Model">Model that wrote the answer.</param>
/// <param name="PromptVersion">System prompt id of the answer.</param>
/// <param name="Usage">Tokens and estimated cost of the answer.</param>
/// <param name="LatencyMs">Time to the end of the answer.</param>
/// <param name="Feedback">The owner's rating of the answer.</param>
public sealed record MessageView(
    Guid Id,
    MessageRole Role,
    string Text,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MovieCard> Movies,
    string? Model,
    string? PromptVersion,
    UsageView? Usage,
    int? LatencyMs,
    FeedbackView? Feedback);

/// <summary>Token usage and estimated cost.</summary>
/// <param name="InputTokens">Input (prompt) tokens.</param>
/// <param name="OutputTokens">Output (completion) tokens.</param>
/// <param name="EstimatedCostUsd">Estimated cost in USD; 0 for models without a price.</param>
public sealed record UsageView(long InputTokens, long OutputTokens, decimal EstimatedCostUsd);

/// <summary>A rating of an answer.</summary>
/// <param name="Rating">1 (thumbs up) or -1 (thumbs down).</param>
/// <param name="Comment">Optional comment.</param>
public sealed record FeedbackView(int Rating, string? Comment);
