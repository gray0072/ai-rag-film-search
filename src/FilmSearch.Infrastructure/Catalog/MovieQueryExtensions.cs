using System.Linq.Expressions;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Localization;

namespace FilmSearch.Infrastructure.Catalog;

/// <summary>
/// Filters, sorting and localized projections over <see cref="Movie"/> queries. Public so that search (I04, I06)
/// applies exactly the same filters inside its own SQL (pre-filtering, spec 05 §1).
/// All expressions translate to SQL; the locale is a query parameter.
/// </summary>
public static class MovieQueryExtensions
{
    public static IQueryable<Movie> ApplyFilter(this IQueryable<Movie> movies, MovieFilter filter)
    {
        if (filter.GenreSlugs.Count > 0)
        {
            var slugs = filter.GenreSlugs;
            movies = movies.Where(m => m.Genres.Any(g => slugs.Contains(g.Slug)));
        }

        if (filter.ExcludedGenreSlugs.Count > 0)
        {
            var excluded = filter.ExcludedGenreSlugs;
            movies = movies.Where(m => !m.Genres.Any(g => excluded.Contains(g.Slug)));
        }

        if (filter.ExcludedMovieIds.Count > 0)
        {
            var excludedIds = filter.ExcludedMovieIds;
            movies = movies.Where(m => !excludedIds.Contains(m.Id));
        }

        if (filter.YearFrom is { } yearFrom)
        {
            movies = movies.Where(m => m.Year >= yearFrom);
        }

        if (filter.YearTo is { } yearTo)
        {
            movies = movies.Where(m => m.Year <= yearTo);
        }

        if (filter.MinRating is { } minRating)
        {
            movies = movies.Where(m => m.VoteAverage >= minRating);
        }

        if (filter.RuntimeMin is { } runtimeMin)
        {
            movies = movies.Where(m => m.RuntimeMinutes >= runtimeMin);
        }

        if (filter.RuntimeMax is { } runtimeMax)
        {
            movies = movies.Where(m => m.RuntimeMinutes <= runtimeMax);
        }

        if (!string.IsNullOrWhiteSpace(filter.Language))
        {
            var language = filter.Language.Trim().ToLowerInvariant();
            movies = movies.Where(m => m.OriginalLanguage == language);
        }

        if (filter.PriceMax is not null || filter.OfferType is not null)
        {
            var priceMax = filter.PriceMax;
            var offerType = filter.OfferType;
            movies = movies.Where(m => m.Offers.Any(o =>
                o.IsActive
                && (offerType == null || o.Type == offerType)
                && (priceMax == null || o.PriceMinor <= priceMax)));
        }

        return movies;
    }

    /// <summary>Sorts with a unique tie-breaker (<see cref="Movie.Id"/>), so paging is stable.</summary>
    public static IOrderedQueryable<Movie> ApplySort(this IQueryable<Movie> movies, MovieSort sort, string locale) =>
        sort switch
        {
            MovieSort.Rating => movies
                .OrderByDescending(m => m.VoteAverage)
                .ThenByDescending(m => m.VoteCount)
                .ThenBy(m => m.Id),
            // PostgreSQL puts NULLs first in descending order: movies without a year go last.
            MovieSort.Year => movies
                .OrderBy(m => m.Year == null)
                .ThenByDescending(m => m.Year)
                .ThenByDescending(m => m.Popularity)
                .ThenBy(m => m.Id),
            MovieSort.Title => movies
                .OrderBy(m => m.Translations.Where(t => t.Locale == locale).Select(t => t.Title).FirstOrDefault() ?? m.Title)
                .ThenBy(m => m.Id),
            _ => movies
                .OrderByDescending(m => m.Popularity)
                .ThenBy(m => m.Id),
        };

    public static Expression<Func<Movie, MovieSummary>> ToSummary(string locale) => m => new MovieSummary(
        m.Id,
        m.Translations.Where(t => t.Locale == locale).Select(t => t.Title).FirstOrDefault() ?? m.Title,
        m.OriginalTitle,
        m.Year,
        m.RuntimeMinutes,
        m.Translations.Where(t => t.Locale == locale).Select(t => t.Overview).FirstOrDefault() ?? m.Overview,
        m.PosterPath,
        m.VoteAverage,
        m.VoteCount,
        m.Genres
            .OrderBy(g => g.NameEn)
            .Select(g => new GenreItem(
                g.Id,
                g.Slug,
                locale == Locales.Russian ? g.NameRu : locale == Locales.Swedish ? g.NameSv : g.NameEn))
            .ToList(),
        m.Offers
            .Where(o => o.IsActive)
            .OrderBy(o => o.Type)
            .Select(o => new OfferItem(o.Id, o.Type, o.PriceMinor, o.Currency, o.RentalHours))
            .ToList());

    public static Expression<Func<Movie, MovieDetails>> ToDetails(string locale) => m => new MovieDetails(
        m.Id,
        m.TmdbId,
        m.ImdbId,
        m.Translations.Where(t => t.Locale == locale).Select(t => t.Title).FirstOrDefault() ?? m.Title,
        m.OriginalTitle,
        m.OriginalLanguage,
        m.ReleaseDate,
        m.Year,
        m.RuntimeMinutes,
        m.Translations.Where(t => t.Locale == locale).Select(t => t.Overview).FirstOrDefault() ?? m.Overview,
        m.Translations.Where(t => t.Locale == locale).Select(t => t.Tagline).FirstOrDefault() ?? m.Tagline,
        m.PosterPath,
        m.BackdropPath,
        m.VoteAverage,
        m.VoteCount,
        m.Keywords,
        m.Genres
            .OrderBy(g => g.NameEn)
            .Select(g => new GenreItem(
                g.Id,
                g.Slug,
                locale == Locales.Russian ? g.NameRu : locale == Locales.Swedish ? g.NameSv : g.NameEn))
            .ToList(),
        m.Credits
            .Where(c => c.Role == CreditRole.Director)
            .OrderBy(c => c.Order)
            .Select(c => new PersonItem(c.PersonId, c.Person.Name))
            .ToList(),
        m.Credits
            .Where(c => c.Role == CreditRole.Writer)
            .OrderBy(c => c.Order)
            .Select(c => new PersonItem(c.PersonId, c.Person.Name))
            .ToList(),
        m.Credits
            .Where(c => c.Role == CreditRole.Cast)
            .OrderBy(c => c.Order)
            .Select(c => new CastItem(c.PersonId, c.Person.Name, c.Character))
            .ToList(),
        m.Offers
            .Where(o => o.IsActive)
            .OrderBy(o => o.Type)
            .Select(o => new OfferItem(o.Id, o.Type, o.PriceMinor, o.Currency, o.RentalHours))
            .ToList());
}
