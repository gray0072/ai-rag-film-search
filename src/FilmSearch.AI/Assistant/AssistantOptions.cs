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

    /// <summary>Movies <c>search_movies</c> returns when the model does not set a limit (at most 10).</summary>
    public int SearchResultLimit { get; set; } = 6;

    /// <summary>Output tokens of the rolling summary.</summary>
    public int SummaryMaxOutputTokens { get; set; } = 400;

    /// <summary>Pinned version of the system prompt; null = latest.</summary>
    public int? PromptVersion { get; set; }

    /// <summary>Pinned version of the summary prompt; null = latest.</summary>
    public int? SummaryPromptVersion { get; set; }
}
