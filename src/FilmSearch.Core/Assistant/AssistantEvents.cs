using System.Text.Json.Serialization;
using FilmSearch.Core.Commerce;

namespace FilmSearch.Core.Assistant;

// Server-sent events of POST /api/chat/conversations/{id}/messages (spec 04 "SSE events"). Every event is written as
// `event: <Type>` and `data: <json of the record>`. cart.updated and confirmation.required come from the cart and
// checkout tools (I12).

/// <summary>An event of an assistant turn. The JSON data carries the event name as <c>type</c> (a discriminated union in the OpenAPI document).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MessageStartedEvent), MessageStartedEvent.Name)]
[JsonDerivedType(typeof(MessageDeltaEvent), MessageDeltaEvent.Name)]
[JsonDerivedType(typeof(ToolStartedEvent), ToolStartedEvent.EventName)]
[JsonDerivedType(typeof(ToolCompletedEvent), ToolCompletedEvent.EventName)]
[JsonDerivedType(typeof(MoviesEvent), MoviesEvent.Name)]
[JsonDerivedType(typeof(CartUpdatedEvent), CartUpdatedEvent.Name)]
[JsonDerivedType(typeof(ConfirmationRequiredEvent), ConfirmationRequiredEvent.Name)]
[JsonDerivedType(typeof(MessageCompletedEvent), MessageCompletedEvent.Name)]
[JsonDerivedType(typeof(ErrorEvent), ErrorEvent.Name)]
public abstract record AssistantEvent
{
    /// <summary>The SSE event name, e.g. <c>message.delta</c>; the same value is the <c>type</c> field of the data.</summary>
    [JsonIgnore]
    public string EventType => GetEventType();

    protected abstract string GetEventType();
}

/// <summary>The answer was started; <paramref name="MessageId"/> is the id of the stored answer (for feedback).</summary>
public sealed record MessageStartedEvent(Guid MessageId) : AssistantEvent
{
    public const string Name = "message.started";

    protected override string GetEventType() => Name;
}

/// <summary>The next chunk of the answer text (markdown).</summary>
public sealed record MessageDeltaEvent(string Text) : AssistantEvent
{
    public const string Name = "message.delta";

    protected override string GetEventType() => Name;
}

/// <summary>A tool call started; the UI shows e.g. "Searching the catalog…".</summary>
public sealed record ToolStartedEvent(string CallId, string Name, string ArgsSummary) : AssistantEvent
{
    public const string EventName = "tool.started";

    protected override string GetEventType() => EventName;
}

/// <summary>A tool call finished; <paramref name="Ok"/> is false when the tool refused the arguments or failed.</summary>
public sealed record ToolCompletedEvent(string CallId, string Name, bool Ok) : AssistantEvent
{
    public const string EventName = "tool.completed";

    protected override string GetEventType() => EventName;
}

/// <summary>Movie cards to show next to the answer, built from tool results (catalog data), not from model text.</summary>
public sealed record MoviesEvent(IReadOnlyList<MovieCard> Items) : AssistantEvent
{
    public const string Name = "movies";

    protected override string GetEventType() => Name;
}

/// <summary>The cart changed through a tool (I12).</summary>
public sealed record CartUpdatedEvent(CartView Cart) : AssistantEvent
{
    public const string Name = "cart.updated";

    protected override string GetEventType() => Name;
}

/// <summary>The assistant asks the user to confirm an action (checkout, I12). Nothing is paid until the user confirms through the confirmations endpoint before <paramref name="ExpiresAt"/>.</summary>
public sealed record ConfirmationRequiredEvent(Guid ConfirmationId, string Kind, ConfirmationSummary Summary, DateTimeOffset ExpiresAt) : AssistantEvent
{
    public const string Name = "confirmation.required";

    protected override string GetEventType() => Name;
}

/// <summary>What the user confirms: the cart lines and the total computed by the server.</summary>
public sealed record ConfirmationSummary(IReadOnlyList<CartLine> Items, long TotalMinor, string Currency);

/// <summary>The answer is complete and stored. <paramref name="Fallback"/> is true when the fallback chat profile wrote it because the active one failed (I19).</summary>
public sealed record MessageCompletedEvent(Guid MessageId, TokenUsage Usage, string Model, long LatencyMs, bool Fallback = false) : AssistantEvent
{
    public const string Name = "message.completed";

    protected override string GetEventType() => Name;
}

/// <summary>Tokens of all model calls of a turn.</summary>
public sealed record TokenUsage(long InputTokens, long OutputTokens);

/// <summary>The turn failed; the stream ends. <paramref name="Code"/> is one of <see cref="AssistantErrorCodes"/>.</summary>
public sealed record ErrorEvent(string Code, string Message) : AssistantEvent
{
    public const string Name = "error";

    protected override string GetEventType() => Name;
}

/// <summary>Codes of <see cref="ErrorEvent"/>.</summary>
public static class AssistantErrorCodes
{
    /// <summary>The chat model is down, timed out or failed.</summary>
    public const string ProviderUnavailable = "providerUnavailable";

    /// <summary>Another answer of the same conversation is still running.</summary>
    public const string ConversationBusy = "conversationBusy";

    /// <summary>The conversation used its token budget (NFR-5).</summary>
    public const string BudgetExceeded = "budgetExceeded";

    /// <summary>The owner used the assistant token budget of the last 24 hours, over all conversations (NFR-5, I18).</summary>
    public const string DailyBudgetExceeded = "dailyBudgetExceeded";

    /// <summary>The whole service used its assistant token budget of the last 24 hours (I20, the demo spend cap).</summary>
    public const string ServiceBudgetExceeded = "serviceBudgetExceeded";

    /// <summary>The active chat profile cannot call tools, so answers could not be grounded in the catalog.</summary>
    public const string ToolsNotSupported = "toolsNotSupported";

    /// <summary>An unexpected error (logged).</summary>
    public const string InternalError = "internalError";
}
