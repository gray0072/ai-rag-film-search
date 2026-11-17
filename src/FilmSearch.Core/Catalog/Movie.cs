namespace FilmSearch.Core.Catalog;

/// <summary>
/// A movie of the catalog. English texts are kept on the movie itself (simple queries, fallback);
/// all locales, English included, are also in <see cref="Translations"/>. Spec 03 §1.
/// </summary>
public sealed class Movie
{
    public Guid Id { get; set; }

    /// <summary>TMDB id: the upsert key for seed and TMDB imports. Null only for manually added movies.</summary>
    public int? TmdbId { get; set; }

    public string? ImdbId { get; set; }

    public required string Title { get; set; }

    public required string OriginalTitle { get; set; }

    /// <summary>ISO 639-1 code of the original language, e.g. <c>en</c>, <c>sv</c>.</summary>
    public required string OriginalLanguage { get; set; }

    public DateOnly? ReleaseDate { get; set; }

    /// <summary>Release year, derived from <see cref="ReleaseDate"/> on write (stored for filtering and sorting).</summary>
    public int? Year { get; set; }

    public int? RuntimeMinutes { get; set; }

    public string? Overview { get; set; }

    public string? Tagline { get; set; }

    /// <summary>TMDB image path such as <c>/abc.jpg</c>. The UI builds the image URL.</summary>
    public string? PosterPath { get; set; }

    public string? BackdropPath { get; set; }

    /// <summary>Average user rating on a 0–10 scale.</summary>
    public decimal VoteAverage { get; set; }

    public int VoteCount { get; set; }

    public double Popularity { get; set; }

    public List<string> Keywords { get; set; } = [];

    public string? AgeRating { get; set; }

    public MovieSource Source { get; set; }

    /// <summary>SHA-256 of the imported data. An upsert with the same hash does not change the movie.</summary>
    public string? SourceHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<MovieTranslation> Translations { get; set; } = [];

    public List<Genre> Genres { get; set; } = [];

    public List<MovieCredit> Credits { get; set; } = [];

    public List<Offer> Offers { get; set; } = [];
}

/// <summary>Texts of a movie in one locale (<c>en</c>, <c>ru</c>, <c>sv</c>). A null field falls back to English.</summary>
public sealed class MovieTranslation
{
    public Guid MovieId { get; set; }

    public required string Locale { get; set; }

    public string? Title { get; set; }

    public string? Overview { get; set; }

    public string? Tagline { get; set; }
}

/// <summary>A genre. <see cref="Id"/> is the TMDB genre id; <see cref="Slug"/> is used in URLs and filters.</summary>
public sealed class Genre
{
    public int Id { get; set; }

    public required string Slug { get; set; }

    public required string NameEn { get; set; }

    public required string NameRu { get; set; }

    public required string NameSv { get; set; }

    public List<Movie> Movies { get; set; } = [];
}

public sealed class Person
{
    public Guid Id { get; set; }

    /// <summary>TMDB person id. Null for people from a hand-made seed (matched by name then).</summary>
    public int? TmdbId { get; set; }

    public required string Name { get; set; }
}

public sealed class MovieCredit
{
    public Guid MovieId { get; set; }

    public Guid PersonId { get; set; }

    public Person Person { get; set; } = null!;

    public CreditRole Role { get; set; }

    public string? Character { get; set; }

    /// <summary>Billing order (cast) or list order (crew), starting at 0.</summary>
    public int Order { get; set; }
}

/// <summary>A way to get a movie: buy (owned forever) or rent (for <see cref="RentalHours"/>). Money in minor units.</summary>
public sealed class Offer
{
    public Guid Id { get; set; }

    public Guid MovieId { get; set; }

    public OfferType Type { get; set; }

    public long PriceMinor { get; set; }

    /// <summary>ISO 4217 currency code, e.g. <c>USD</c>.</summary>
    public required string Currency { get; set; }

    public int? RentalHours { get; set; }

    public bool IsActive { get; set; } = true;
}
