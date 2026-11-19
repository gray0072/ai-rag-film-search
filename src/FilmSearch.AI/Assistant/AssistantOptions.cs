namespace FilmSearch.AI.Assistant;

/// <summary>The <c>Assistant</c> configuration section (spec 06 §2, §6; NFR-5).</summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>Model calls with tools per turn. When they are used up, one more call without tools writes the answer.</summary>
    public int MaxToolIterations { get; set; } = 6;

    /// <summary>Visible messages (user messages and answers) of earlier turns sent to the model.</summary>
    public int HistoryMaxMessages { get; set; } = 12;

    /// <summary>
    /// Estimated tokens of earlier turns sent to the model. Older turns beyond this budget or beyond
    /// <see cref="HistoryMaxMessages"/> are folded into the rolling summary of the conversation.
    /// </summary>
    public int HistoryTokenBudget { get; set; } = 3000;

    /// <summary>Token budget of one conversation (input + output of all calls). A turn past it is refused.</summary>
    public long MaxConversationTokens { get; set; } = 300_000;

    /// <summary>
    /// Token budget of one owner (anonymous session or user) over all conversations in the last 24 hours, so that new
    /// conversations do not get around <see cref="MaxConversationTokens"/>. Counts the tokens stored with answers.
    /// </summary>
    public long MaxOwnerTokensPerDay { get; set; } = 1_000_000;

    /// <summary>
    /// Token budget of the whole service (all owners) over the last 24 hours; 0 = off. A public demo gets a new anonymous
    /// owner per browser, so only this caps the spend on a paid model (I20). Counts the tokens stored with answers.
    /// </summary>
    public long MaxTotalTokensPerDay { get; set; }

    /// <summary>Movies <c>search_movies</c> returns when the model does not set a limit (at most 10).</summary>
    public int SearchResultLimit { get; set; } = 6;

    /// <summary>Minutes the user has to press the checkout button of a <c>request_checkout</c> confirmation.</summary>
    public int ConfirmationTtlMinutes { get; set; } = 15;

    /// <summary>Output tokens of the rolling summary.</summary>
    public int SummaryMaxOutputTokens { get; set; } = 400;

    /// <summary>Pinned version of the system prompt; null = latest.</summary>
    public int? PromptVersion { get; set; }

    /// <summary>Pinned version of the summary prompt; null = latest.</summary>
    public int? SummaryPromptVersion { get; set; }
}
