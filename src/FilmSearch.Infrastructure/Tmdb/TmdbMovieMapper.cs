using System.Globalization;
using System.Text;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Localization;

namespace FilmSearch.Infrastructure.Tmdb;

/// <summary>
/// TMDB DTOs → import model. The single mapping path for the seed builder and the TMDB sync (spec 08 §3).
/// The movie details must be requested with <c>language=en-US</c> and
/// <c>append_to_response=credits,keywords,translations,external_ids</c>.
/// </summary>
public static class TmdbMovieMapper
{
    public const int MaxCast = 10;
    public const int MaxWriters = 5;
    public const int MaxKeywords = 20;

    /// <summary>Region preferred when TMDB has several translations for one language.</summary>
    private static readonly Dictionary<string, string> PreferredRegion = new(StringComparer.Ordinal)
    {
        [Locales.Russian] = "RU",
        [Locales.Swedish] = "SE",
    };

    private static readonly HashSet<string> WriterJobs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Screenplay", "Writer", "Story", "Novel", "Author", "Characters",
    };

    public static MovieImport ToImport(TmdbMovieDetails movie)
    {
        var title = Clean(movie.Title) ?? Clean(movie.OriginalTitle)
            ?? throw new ArgumentException($"TMDB movie {movie.Id} has no title.", nameof(movie));

        var translations = new Dictionary<string, TranslationImport>(StringComparer.Ordinal)
        {
            [Locales.English] = new(title, Clean(movie.Overview), Clean(movie.Tagline)),
        };
        foreach (var locale in Locales.All.Where(l => l != Locales.English))
        {
            if (FindTranslation(movie, locale) is { } translation)
            {
                translations[locale] = translation;
            }
        }

        return new MovieImport
        {
            TmdbId = movie.Id,
            ImdbId = Clean(movie.ImdbId) ?? Clean(movie.ExternalIds?.ImdbId),
            OriginalTitle = Clean(movie.OriginalTitle) ?? title,
            OriginalLanguage = Clean(movie.OriginalLanguage)?.ToLowerInvariant() ?? "en",
            ReleaseDate = DateOnly.TryParseExact(movie.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null,
            RuntimeMinutes = movie.Runtime is > 0 ? movie.Runtime : null,
            PosterPath = Clean(movie.PosterPath),
            BackdropPath = Clean(movie.BackdropPath),
            VoteAverage = Math.Round((decimal)movie.VoteAverage, 1),
            VoteCount = movie.VoteCount,
            Popularity = Math.Round(movie.Popularity, 3),
            GenreIds = [.. movie.Genres.Select(g => g.Id)],
            Keywords = [.. (movie.Keywords?.Keywords ?? []).Select(k => k.Name.Trim()).Where(k => k.Length > 0).Distinct().Take(MaxKeywords)],
            Translations = translations,
            Credits = MapCredits(movie.Credits),
        };
    }

    public static GenreImport ToGenre(int id, IReadOnlyDictionary<string, string> namesByLocale)
    {
        var english = namesByLocale.GetValueOrDefault(Locales.English)
            ?? throw new ArgumentException($"Genre {id} has no English name.", nameof(namesByLocale));
        return new GenreImport(id, Slugify(english), namesByLocale);
    }

    /// <summary><c>Science Fiction</c> → <c>science-fiction</c>, <c>TV Movie</c> → <c>tv-movie</c>.</summary>
    public static string Slugify(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    private static TranslationImport? FindTranslation(TmdbMovieDetails movie, string locale)
    {
        var candidates = (movie.Translations?.Translations ?? [])
            .Where(t => string.Equals(t.Iso6391, locale, StringComparison.OrdinalIgnoreCase) && t.Data is not null)
            .OrderByDescending(t => string.Equals(t.Iso31661, PreferredRegion.GetValueOrDefault(locale), StringComparison.OrdinalIgnoreCase))
            .Select(t => new TranslationImport(Clean(t.Data!.Title), Clean(t.Data.Overview), Clean(t.Data.Tagline)))
            .Where(t => t.Title is not null || t.Overview is not null);

        return candidates.FirstOrDefault();
    }

    private static List<CreditImport> MapCredits(TmdbCredits? credits)
    {
        var result = new List<CreditImport>();
        var crew = credits?.Crew ?? [];

        var directors = crew.Where(c => string.Equals(c.Job, "Director", StringComparison.OrdinalIgnoreCase)).DistinctBy(c => c.Id);
        result.AddRange(directors.Select((c, i) => new CreditImport(c.Id, c.Name.Trim(), CreditRole.Director, null, i)));

        var writers = crew
            .Where(c => string.Equals(c.Department, "Writing", StringComparison.OrdinalIgnoreCase) && c.Job is not null && WriterJobs.Contains(c.Job))
            .DistinctBy(c => c.Id)
            .Take(MaxWriters);
        result.AddRange(writers.Select((c, i) => new CreditImport(c.Id, c.Name.Trim(), CreditRole.Writer, null, i)));

        var cast = (credits?.Cast ?? []).OrderBy(c => c.Order).DistinctBy(c => c.Id).Take(MaxCast);
        result.AddRange(cast.Select((c, i) => new CreditImport(c.Id, c.Name.Trim(), CreditRole.Cast, Clean(c.Character), i)));

        return result;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
