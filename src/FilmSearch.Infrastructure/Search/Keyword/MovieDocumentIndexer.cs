using FilmSearch.Core.Catalog;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FilmSearch.Infrastructure.Search.Keyword;

/// <summary>
/// Keeps <see cref="MovieSearchDocument"/> rows in sync with the catalog: runs inside the catalog write transaction
/// and rebuilds the documents of the changed movies with the SQL function <c>search.refresh_movie_documents</c>
/// (created by migration <c>I04_AddKeywordSearch</c>, which also used it to fill the table for existing movies).
/// The flattening lives in SQL so the migration backfill and later updates share one definition.
/// </summary>
internal sealed class MovieDocumentIndexer(AppDbContext db) : ICatalogChangeHandler
{
    public async Task OnMoviesChangedAsync(IReadOnlyCollection<Guid> movieIds, CancellationToken cancellationToken)
    {
        if (movieIds.Count == 0)
        {
            return;
        }

        var ids = movieIds.ToArray();
        await db.Database.ExecuteSqlAsync($"SELECT search.refresh_movie_documents({ids})", cancellationToken);
    }
}
