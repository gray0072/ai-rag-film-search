namespace FilmSearch.AI.QueryUnderstanding;

/// <summary>Configuration section <c>Search:QueryUnderstanding</c> (spec 05 §5).</summary>
public sealed class QueryUnderstandingOptions
{
    public const string SectionName = "Search:QueryUnderstanding";

    /// <summary>Prompt version of <c>query-understanding</c>; null takes the latest embedded version.</summary>
    public int? PromptVersion { get; set; }

    /// <summary>Chat profile for the call; null uses the active chat profile.</summary>
    public string? ChatProfile { get; set; }

    /// <summary>How long a valid result is cached per normalized query.</summary>
    public int CacheMinutes { get; set; } = 60;

    /// <summary>Output token limit of one call (the JSON is short; a limit stops a model that loops).</summary>
    public int MaxOutputTokens { get; set; } = 400;
}
