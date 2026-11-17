namespace FilmSearch.Core.Catalog;

// Import model: the shape of the seed file (data/seed/movies.seed.json) and the target of the TMDB mapping.
// One mapping path for every source: source DTO -> MovieImport -> ICatalogWriter (spec 08 §3).

/// <summary>A genre with names per locale (<c>en</c>, <c>ru</c>, <c>sv</c>).</summary>
public sealed record GenreImport(int Id, string Slug, IReadOnlyDictionary<string, string> Names);

public sealed record MovieImport
{
    public required int TmdbId { get; init; }

    public string? ImdbId { get; init; }

    public required string OriginalTitle { get; init; }

    public required string OriginalLanguage { get; init; }

    public DateOnly? ReleaseDate { get; init; }

    public int? RuntimeMinutes { get; init; }

    public string? PosterPath { get; init; }

    public string? BackdropPath { get; init; }

    public decimal VoteAverage { get; init; }

    public int VoteCount { get; init; }

    public double Popularity { get; init; }

    public IReadOnlyList<int> GenreIds { get; init; } = [];

    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Texts by locale. <c>en</c> is required: it is the fallback for the other locales.</summary>
    public required IReadOnlyDictionary<string, TranslationImport> Translations { get; init; }

    public IReadOnlyList<CreditImport> Credits { get; init; } = [];
}

public sealed record TranslationImport(string? Title, string? Overview, string? Tagline);

/// <summary>A credit. <see cref="PersonTmdbId"/> is null in a hand-made seed; the person is matched by name then.</summary>
public sealed record CreditImport(int? PersonTmdbId, string Name, CreditRole Role, string? Character, int Order);
