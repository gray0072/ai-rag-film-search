using System.Text.Json;
using System.Text.Json.Serialization;

namespace FilmSearch.Infrastructure.Tmdb;

// TMDB API v3 response DTOs (only the fields we use). Used by tools/seed-builder and by TmdbClient (the import job, I16).
// https://developer.themoviedb.org/reference/movie-details

public static class TmdbJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}

public sealed record TmdbPage<T>(int Page, int TotalPages, IReadOnlyList<T> Results);

public sealed record TmdbListItem(int Id, bool Adult, int VoteCount);

public sealed record TmdbGenreList(IReadOnlyList<TmdbGenre> Genres);

public sealed record TmdbGenre(int Id, string Name);

public sealed record TmdbMovieDetails
{
    public int Id { get; init; }

    public string? ImdbId { get; init; }

    public string? Title { get; init; }

    public string? OriginalTitle { get; init; }

    public string? OriginalLanguage { get; init; }

    public string? Overview { get; init; }

    public string? Tagline { get; init; }

    /// <summary><c>yyyy-MM-dd</c>, or an empty string when unknown.</summary>
    public string? ReleaseDate { get; init; }

    public int? Runtime { get; init; }

    public string? PosterPath { get; init; }

    public string? BackdropPath { get; init; }

    public double VoteAverage { get; init; }

    public int VoteCount { get; init; }

    public double Popularity { get; init; }

    public bool Adult { get; init; }

    public IReadOnlyList<TmdbGenre> Genres { get; init; } = [];

    public TmdbCredits? Credits { get; init; }

    public TmdbKeywords? Keywords { get; init; }

    public TmdbTranslations? Translations { get; init; }

    public TmdbExternalIds? ExternalIds { get; init; }
}

public sealed record TmdbCredits(IReadOnlyList<TmdbCast>? Cast, IReadOnlyList<TmdbCrew>? Crew);

public sealed record TmdbCast(int Id, string Name, string? Character, int Order);

public sealed record TmdbCrew(int Id, string Name, string? Job, string? Department);

public sealed record TmdbKeywords(IReadOnlyList<TmdbKeyword>? Keywords);

public sealed record TmdbKeyword(int Id, string Name);

public sealed record TmdbTranslations(IReadOnlyList<TmdbTranslation>? Translations);

public sealed record TmdbTranslation(
    [property: JsonPropertyName("iso_3166_1")] string? Iso31661,
    [property: JsonPropertyName("iso_639_1")] string? Iso6391,
    TmdbTranslationData? Data);

public sealed record TmdbTranslationData(string? Title, string? Overview, string? Tagline);

public sealed record TmdbExternalIds(string? ImdbId);
