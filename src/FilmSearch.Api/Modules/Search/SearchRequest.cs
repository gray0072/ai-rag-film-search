using System.ComponentModel.DataAnnotations;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;

namespace FilmSearch.Api.Modules.Search;

/// <summary>Body of <c>POST /api/search</c> (spec 04 "Search"). Validated at the boundary; invalid input → 400 Problem Details.</summary>
public sealed class SearchRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    /// <summary>Free text in English, Russian or Swedish.</summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public string Query { get; init; } = string.Empty;

    /// <summary><c>keyword</c> (default). <c>semantic</c> and <c>hybrid</c> return 400 until I06 / I07.</summary>
    public SearchMode? Mode { get; init; }

    /// <summary>The same filters as <c>GET /api/movies</c>, applied inside the search query.</summary>
    public SearchFilters? Filters { get; init; }

    /// <summary>Run query understanding (FR-SRCH-4). Not available until I07: <c>true</c> returns 400.</summary>
    public bool Understand { get; init; }

    [Range(1, 10_000)]
    public int? Page { get; init; }

    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; }

    /// <summary>Include per-item retrieval scores (<c>debug</c>).</summary>
    public bool Debug { get; init; }

    public SearchQuery ToQuery(string locale) => new(
        Query.Trim(),
        Mode ?? SearchMode.Keyword,
        (Filters ?? new SearchFilters()).ToFilter(),
        locale,
        Page ?? 1,
        PageSize ?? DefaultPageSize,
        Debug);
}

/// <summary>Catalog filters in a JSON body (FR-SRCH-5). Same meaning and limits as the <c>GET /api/movies</c> query.</summary>
public sealed class SearchFilters : IValidatableObject
{
    /// <summary>Genre slugs; a movie matches any of them.</summary>
    [MaxLength(30)]
    public IReadOnlyList<string>? Genres { get; init; }

    [Range(1870, 2100)]
    public int? YearFrom { get; init; }

    [Range(1870, 2100)]
    public int? YearTo { get; init; }

    [Range(0.0, 10.0)]
    public decimal? MinRating { get; init; }

    [Range(1, 1000)]
    public int? RuntimeMin { get; init; }

    [Range(1, 1000)]
    public int? RuntimeMax { get; init; }

    /// <summary>Original language, ISO 639-1 (e.g. <c>en</c>, <c>sv</c>).</summary>
    [StringLength(3, MinimumLength = 2)]
    public string? Language { get; init; }

    /// <summary>Maximum price in minor units (cents).</summary>
    [Range(0, 1_000_000)]
    public long? PriceMax { get; init; }

    public OfferType? OfferType { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (YearFrom > YearTo)
        {
            yield return new ValidationResult("yearFrom must not be greater than yearTo.", [nameof(YearFrom)]);
        }

        if (RuntimeMin > RuntimeMax)
        {
            yield return new ValidationResult("runtimeMin must not be greater than runtimeMax.", [nameof(RuntimeMin)]);
        }

        if (Genres?.Any(g => string.IsNullOrWhiteSpace(g) || g.Length > 64) == true)
        {
            yield return new ValidationResult("genres must be non-empty slugs of at most 64 characters.", [nameof(Genres)]);
        }
    }

    public MovieFilter ToFilter() => new()
    {
        GenreSlugs = Genres is null ? [] : [.. Genres.Select(g => g.Trim().ToLowerInvariant()).Distinct()],
        YearFrom = YearFrom,
        YearTo = YearTo,
        MinRating = MinRating,
        RuntimeMin = RuntimeMin,
        RuntimeMax = RuntimeMax,
        Language = string.IsNullOrWhiteSpace(Language) ? null : Language.Trim().ToLowerInvariant(),
        PriceMax = PriceMax,
        OfferType = OfferType,
    };

    public static SearchFilters From(MovieFilter filter) => new()
    {
        Genres = filter.GenreSlugs,
        YearFrom = filter.YearFrom,
        YearTo = filter.YearTo,
        MinRating = filter.MinRating,
        RuntimeMin = filter.RuntimeMin,
        RuntimeMax = filter.RuntimeMax,
        Language = filter.Language,
        PriceMax = filter.PriceMax,
        OfferType = filter.OfferType,
    };
}

/// <param name="Items">Found movies, best first.</param>
/// <param name="AppliedFilters">The filters the search used, normalized (later also the ones found by query understanding).</param>
/// <param name="Page">Page number, starting at 1.</param>
/// <param name="PageSize">Items per page.</param>
/// <param name="Total">Number of matching movies.</param>
public sealed record SearchResponse(
    IReadOnlyList<SearchItem> Items,
    SearchFilters AppliedFilters,
    int Page,
    int PageSize,
    int Total);
