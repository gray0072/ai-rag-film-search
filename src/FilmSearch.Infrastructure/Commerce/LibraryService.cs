using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using FilmSearch.Core.Owners;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FilmSearch.Infrastructure.Commerce;

/// <summary>"My library" (FR-COM-6): active entitlements with localized movie texts from the catalog.</summary>
internal sealed class LibraryService(AppDbContext db, ICatalogReader catalog, IOwnerContext owner, TimeProvider time) : ILibraryService
{
    public async Task<IReadOnlyList<LibraryItem>> GetLibraryAsync(string locale, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var rows = await (
                from e in db.Set<Entitlement>().AsNoTracking()
                join i in db.Set<OrderItem>() on new { e.OrderId, e.MovieId } equals new { i.OrderId, i.MovieId }
                where e.OwnerId == owner.OwnerId
                    && e.RevokedAt == null
                    && e.ValidFrom <= now
                    && (e.ValidUntil == null || e.ValidUntil > now)
                select new { Entitlement = e, i.TitleSnapshot })
            .ToListAsync(cancellationToken);

        // One entry per movie: owned wins over a rental; of two rentals the one that lasts longer.
        var best = rows
            .GroupBy(r => r.Entitlement.MovieId)
            .Select(g => g
                .OrderBy(r => r.Entitlement.Type == EntitlementType.Owned ? 0 : 1)
                .ThenByDescending(r => r.Entitlement.ValidUntil)
                .First())
            .OrderByDescending(r => r.Entitlement.ValidFrom)
            .ToList();

        var movies = (await catalog.GetMoviesAsync([.. best.Select(r => r.Entitlement.MovieId)], locale, cancellationToken))
            .ToDictionary(m => m.Id);

        return [.. best.Select(r =>
        {
            var movie = movies.GetValueOrDefault(r.Entitlement.MovieId);
            return new LibraryItem(
                r.Entitlement.MovieId,
                movie?.Title ?? r.TitleSnapshot,
                movie?.Year,
                movie?.PosterPath,
                r.Entitlement.Type,
                r.Entitlement.ValidFrom,
                r.Entitlement.ValidUntil,
                r.Entitlement.OrderId);
        })];
    }
}
