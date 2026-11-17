using FilmSearch.Core.Catalog;
using FilmSearch.Core.Localization;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FilmSearch.Infrastructure.Catalog;

internal sealed class CatalogReader(AppDbContext db) : ICatalogReader
{
    public async Task<PagedResult<MovieSummary>> ListMoviesAsync(MovieListQuery query, string locale, CancellationToken cancellationToken)
    {
        locale = Locales.Normalize(locale);
        var movies = db.Set<Movie>().AsNoTracking().ApplyFilter(query.Filter);

        var total = await movies.CountAsync(cancellationToken);
        var items = total == 0
            ? []
            : await movies
                .ApplySort(query.Sort, locale)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(MovieQueryExtensions.ToSummary(locale))
                .AsSplitQuery()
                .ToListAsync(cancellationToken);

        return new PagedResult<MovieSummary>(items, query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<MovieSummary>> GetMoviesAsync(IReadOnlyList<Guid> ids, string locale, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        locale = Locales.Normalize(locale);
        var found = await db.Set<Movie>()
            .AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(MovieQueryExtensions.ToSummary(locale))
            .AsSplitQuery()
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        return [.. ids.Distinct().Where(found.ContainsKey).Select(id => found[id])];
    }

    public Task<MovieDetails?> GetMovieAsync(Guid id, string locale, CancellationToken cancellationToken)
    {
        locale = Locales.Normalize(locale);
        return db.Set<Movie>()
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(MovieQueryExtensions.ToDetails(locale))
            .AsSplitQuery()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MovieDetails>> GetMovieDetailsAsync(IReadOnlyList<Guid> ids, string locale, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        locale = Locales.Normalize(locale);
        var found = await db.Set<Movie>()
            .AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(MovieQueryExtensions.ToDetails(locale))
            .AsSplitQuery()
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        return [.. ids.Distinct().Where(found.ContainsKey).Select(id => found[id])];
    }

    public async Task<IReadOnlyList<Guid>> GetMovieIdsAsync(CancellationToken cancellationToken) =>
        await db.Set<Movie>().AsNoTracking().OrderBy(m => m.Id).Select(m => m.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GenreItem>> ListGenresAsync(string locale, CancellationToken cancellationToken)
    {
        locale = Locales.Normalize(locale);
        return await db.Set<Genre>()
            .AsNoTracking()
            .Select(g => new
            {
                g.Id,
                g.Slug,
                Name = locale == Locales.Russian ? g.NameRu : locale == Locales.Swedish ? g.NameSv : g.NameEn,
            })
            .OrderBy(g => g.Name)
            .Select(g => new GenreItem(g.Id, g.Slug, g.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasMoviesAsync(CancellationToken cancellationToken) =>
        db.Set<Movie>().AnyAsync(cancellationToken);
}
