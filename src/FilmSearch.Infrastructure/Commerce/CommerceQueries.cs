using FilmSearch.Core.Commerce;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FilmSearch.Infrastructure.Commerce;

internal static class CommerceQueries
{
    /// <summary>Active (not revoked, not expired) entitlements of the owner for the given movies, by movie id.</summary>
    public static async Task<ILookup<Guid, Entitlement>> ActiveEntitlementsAsync(
        this AppDbContext db,
        Guid ownerId,
        IReadOnlyCollection<Guid> movieIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var entitlements = await db.Set<Entitlement>()
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                && movieIds.Contains(e.MovieId)
                && e.RevokedAt == null
                && e.ValidFrom <= now
                && (e.ValidUntil == null || e.ValidUntil > now))
            .ToListAsync(cancellationToken);

        return entitlements.ToLookup(e => e.MovieId);
    }

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
