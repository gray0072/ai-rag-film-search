namespace FilmSearch.Core.Assistant;

/// <summary>A chat with the assistant (spec 03 "Assistant"). Belongs to one owner; the full history is stored.</summary>
public sealed class Conversation
{
    public Guid Id { get; set; }

    /// <summary>User id or anonymous session id (<see cref="Owners.IOwnerContext"/>).</summary>
    public Guid OwnerId { get; set; }

    /// <summary>The beginning of the first user message; null until the first message.</summary>
    public string? Title { get; set; }

    /// <summary>Locale of movie texts when a request has no <c>Accept-Language</c> header (<c>en</c>, <c>ru</c>, <c>sv</c>).</summary>
    public required string Locale { get; set; }

    /// <summary>Chat profile of the last turn (<c>AI:ChatProfiles</c>).</summary>
    public required string ProfileName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>All input tokens of the conversation: answers and summaries (NFR-5).</summary>
    public long TotalInputTokens { get; set; }

    public long TotalOutputTokens { get; set; }

    /// <summary>Sum of the estimated costs; 0 for models without a price (local models).</summary>
    public decimal EstimatedCostUsd { get; set; }

    /// <summary>Rolling summary of the messages up to <see cref="SummarizedThroughSeq"/> (spec 06 §6).</summary>
    public string? Summary { get; set; }

    /// <summary>The last message covered by <see cref="Summary"/>; 0 when nothing is summarized.</summary>
    public int SummarizedThroughSeq { get; set; }

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public void AddUsage(long inputTokens, long outputTokens, decimal? costUsd)
    {
        TotalInputTokens += inputTokens;
        TotalOutputTokens += outputTokens;
        EstimatedCostUsd += costUsd ?? 0;
    }
}

public enum MessageRole
{
    User,
    Assistant,
    Tool,
    System,
}

/// <summary>
/// One message of a conversation (spec 03 <c>ChatMessage</c>; named differently to avoid a clash with
/// <c>Microsoft.Extensions.AI.ChatMessage</c>). A turn is stored as: the user message, for every tool round an
/// assistant message with <see cref="ToolCallsJson"/> and one tool message per call, then the answer: an assistant
/// message without tool calls that holds all the text the user saw, the usage and the movie cards.
/// </summary>
public sealed class ConversationMessage
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    /// <summary>Order in the conversation, starting at 1.</summary>
    public int Seq { get; set; }

    public MessageRole Role { get; set; }

    /// <summary>Text of a user or assistant message; the JSON result of a tool message.</summary>
    public string Content { get; set; } = "";

    /// <summary>Tool calls of an assistant message: <c>[{ callId, name, arguments }]</c>.</summary>
    public string? ToolCallsJson { get; set; }

    /// <summary>The call a tool message answers.</summary>
    public string? ToolCallId { get; set; }

    /// <summary>Model that wrote the answer.</summary>
    public string? Model { get; set; }

    /// <summary>Input tokens of all model calls of the turn (answers only).</summary>
    public long? InputTokens { get; set; }

    public long? OutputTokens { get; set; }

    public decimal? EstimatedCostUsd { get; set; }

    /// <summary>Time from the user message to the end of the answer.</summary>
    public int? LatencyMs { get; set; }

    /// <summary>System prompt id, e.g. <c>assistant-system.v1</c> (answers only).</summary>
    public string? PromptVersion { get; set; }

    /// <summary>Movies shown as cards with the answer, in order. Cards are built from the catalog when the message is read.</summary>
    public List<Guid> MovieIds { get; set; } = [];

    /// <summary>OpenTelemetry trace id (32 hex characters) of the turn that wrote the answer: the link from feedback to the trace (I18).</summary>
    public string? TraceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>A message the user sees: a user message or an answer (not a tool round).</summary>
    public bool IsVisible => Role == MessageRole.User || (Role == MessageRole.Assistant && ToolCallsJson is null);
}

/// <summary>Thumbs up / down on an answer (FR-AST-9). One per message; a new rating replaces the old one.</summary>
public sealed class MessageFeedback
{
    public Guid MessageId { get; set; }

    /// <summary>+1 or −1.</summary>
    public int Rating { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
