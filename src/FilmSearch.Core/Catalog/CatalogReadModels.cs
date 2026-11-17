namespace FilmSearch.Core.Catalog;

// Read models returned by ICatalogReader. Texts are already localized (requested locale, falling back to English).

public sealed record GenreItem(int Id, string Slug, string Name);

public sealed record OfferItem(Guid Id, OfferType Type, long PriceMinor, string Currency, int? RentalHours);

public sealed record MovieSummary(
    Guid Id,
    string Title,
    string OriginalTitle,
    int? Year,
    int? RuntimeMinutes,
    string? Overview,
    string? PosterPath,
    decimal VoteAverage,
    int VoteCount,
    IReadOnlyList<GenreItem> Genres,
    IReadOnlyList<OfferItem> Offers);

public sealed record PersonItem(Guid Id, string Name);

public sealed record CastItem(Guid Id, string Name, string? Character);

public sealed record MovieDetails(
    Guid Id,
    int? TmdbId,
    string? ImdbId,
    string Title,
    string OriginalTitle,
    string OriginalLanguage,
    DateOnly? ReleaseDate,
    int? Year,
    int? RuntimeMinutes,
    string? Overview,
    string? Tagline,
    string? PosterPath,
    string? BackdropPath,
    decimal VoteAverage,
    int VoteCount,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<GenreItem> Genres,
    IReadOnlyList<PersonItem> Directors,
    IReadOnlyList<PersonItem> Writers,
    IReadOnlyList<CastItem> Cast,
    IReadOnlyList<OfferItem> Offers);
