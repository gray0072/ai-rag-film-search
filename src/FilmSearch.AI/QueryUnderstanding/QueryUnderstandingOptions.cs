namespace FilmSearch.AI.QueryUnderstanding;

/// <summary>Configuration section <c>Search:QueryUnderstanding</c> (spec 05 §5).</summary>
public sealed class QueryUnderstandingOptions
{
    public const string SectionName = "Search:QueryUnderstanding";

    /// <summary>Prompt version of <c>query-understanding</c>; null takes the latest embedded version.</summary>
    public int? PromptVersion { get; set; }

    /// <summary>Chat profile for the call; null uses the active chat profile.</summary>
    public string? ChatProfile { get; set; }

    /// <summary>
    /// How long a valid result is cached per normalized query. A day (I19): the answer depends only on the query, the
    /// prompt, the model and the catalog's genres, which all change rarely. 0 turns the cache off.
    /// </summary>
    public int CacheMinutes { get; set; } = 24 * 60;

    /// <summary>Most cached results (a few hundred bytes each).</summary>
    public int CacheMaxEntries { get; set; } = 10_000;

    /// <summary>Output token limit of one call (the JSON is short; a limit stops a model that loops).</summary>
    public int MaxOutputTokens { get; set; } = 400;
}
