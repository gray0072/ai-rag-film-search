namespace FilmSearch.Core.Catalog;

/// <summary>
/// Catalog filters (FR-CAT-2). Search modes (I04, I06) apply the same filters inside their queries (FR-SRCH-5).
/// All set conditions must match; <see cref="GenreSlugs"/> matches any of the genres.
/// </summary>
public sealed record MovieFilter
{
    public static MovieFilter None { get; } = new();

    public IReadOnlyList<string> GenreSlugs { get; init; } = [];

    public int? YearFrom { get; init; }

    public int? YearTo { get; init; }

    public decimal? MinRating { get; init; }

    public int? RuntimeMin { get; init; }

    public int? RuntimeMax { get; init; }

    /// <summary>Original language, ISO 639-1.</summary>
    public string? Language { get; init; }

    /// <summary>Maximum price in minor units: the movie must have an active offer at or below it.</summary>
    public long? PriceMax { get; init; }

    /// <summary>The movie must have an active offer of this type (combined with <see cref="PriceMax"/> on the same offer).</summary>
    public OfferType? OfferType { get; init; }

    /// <summary>True when at least one condition is set (a method, so it is not serialized with the filter).</summary>
    public bool HasConditions() =>
        GenreSlugs.Count > 0
        || YearFrom is not null
        || YearTo is not null
        || MinRating is not null
        || RuntimeMin is not null
        || RuntimeMax is not null
        || !string.IsNullOrWhiteSpace(Language)
        || PriceMax is not null
        || OfferType is not null;
}

public sealed record MovieListQuery(MovieFilter Filter, MovieSort Sort, int Page, int PageSize);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
