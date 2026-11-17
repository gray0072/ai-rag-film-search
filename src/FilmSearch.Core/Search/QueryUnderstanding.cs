using FilmSearch.Core.Catalog;

namespace FilmSearch.Core.Search;

/// <summary>
/// Query understanding (spec 05 §5, FR-SRCH-4): an LLM turns free text ("фильмы 90-х про космос с рейтингом выше 7, без
/// ужасов") into catalog filters and a short semantic text. The implementation in <c>FilmSearch.AI</c> validates the model
/// output, retries once with the error and falls back to "no filters, the whole query as semantic text", so a bad answer
/// or a provider outage never breaks search.
/// </summary>
public interface IQueryUnderstanding
{
    /// <summary>Never throws for model or provider failures: returns a <see cref="QueryUnderstandingSource.Fallback"/> result instead.</summary>
    Task<UnderstoodQuery> UnderstandAsync(string query, CancellationToken cancellationToken);
}

/// <summary>Where an <see cref="UnderstoodQuery"/> came from.</summary>
public enum QueryUnderstandingSource
{
    /// <summary>A valid answer of the model.</summary>
    Llm,

    /// <summary>An earlier valid answer for the same normalized query (memory cache).</summary>
    Cache,

    /// <summary>The model failed or returned invalid output twice: no filters, the whole query as semantic text.</summary>
    Fallback,
}

// Doc comments of types in the OpenAPI document stay on one line (see SearchModels.cs).

/// <summary>Result of query understanding: the text to search for, the filters found in the query and the titles of movies the user wants something similar to.</summary>
public sealed record UnderstoodQuery
{
    /// <summary>What to search for, without the parts that became filters (English).</summary>
    public required string SemanticQuery { get; init; }

    public UnderstoodFilters Filters { get; init; } = new();

    /// <summary>Titles from "movies like X"; resolved to catalog movies by keyword search.</summary>
    public IReadOnlyList<string> SimilarToTitles { get; init; } = [];

    /// <summary>Language of the query (<c>en</c>, <c>ru</c>, <c>sv</c>) when the model recognized it.</summary>
    public string? DetectedLocale { get; init; }

    public QueryUnderstandingSource Source { get; init; }

    /// <summary>Prompt that produced the result, e.g. <c>query-understanding.v1</c>.</summary>
    public string? PromptVersion { get; init; }

    /// <summary>Why the result is a fallback (validation or provider error), for debugging.</summary>
    public string? Error { get; init; }

    public static UnderstoodQuery Fallback(string query, string? promptVersion, string error) => new()
    {
        SemanticQuery = query,
        Source = QueryUnderstandingSource.Fallback,
        PromptVersion = promptVersion,
        Error = error,
    };
}

/// <summary>Filters found by query understanding. Null or empty means "not mentioned".</summary>
public sealed record UnderstoodFilters
{
    /// <summary>Genre slugs the movie should have (any of them).</summary>
    public IReadOnlyList<string> GenresInclude { get; init; } = [];

    /// <summary>Genre slugs the movie must not have.</summary>
    public IReadOnlyList<string> GenresExclude { get; init; } = [];

    public int? YearFrom { get; init; }

    public int? YearTo { get; init; }

    public decimal? MinRating { get; init; }

    /// <summary>Maximum price in minor units (cents).</summary>
    public long? MaxPriceMinor { get; init; }

    /// <summary>Original language, ISO 639-1.</summary>
    public string? Language { get; init; }

    /// <summary>
    /// Combines these filters with the filters of the request. A filter the user set explicitly (UI) wins over the same
    /// filter found in the text; excluded genres are added to the explicit ones.
    /// </summary>
    public MovieFilter MergeInto(MovieFilter explicitFilter) => explicitFilter with
    {
        GenreSlugs = explicitFilter.GenreSlugs.Count > 0 ? explicitFilter.GenreSlugs : GenresInclude,
        ExcludedGenreSlugs = [.. explicitFilter.ExcludedGenreSlugs.Union(GenresExclude, StringComparer.Ordinal)],
        YearFrom = explicitFilter.YearFrom ?? YearFrom,
        YearTo = explicitFilter.YearTo ?? YearTo,
        MinRating = explicitFilter.MinRating ?? MinRating,
        PriceMax = explicitFilter.PriceMax ?? MaxPriceMinor,
        Language = explicitFilter.Language ?? Language,
    };
}
