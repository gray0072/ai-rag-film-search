using FilmSearch.Core.Commerce;
using FilmSearch.Core.Owners;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FilmSearch.Infrastructure.Commerce;

/// <summary>
/// Login merge of the Commerce module (FR-USR-2): orders and entitlements move to the user; cart lines move into the
/// user's cart (for a movie in both carts the newer line wins), then lines the account can no longer buy (an owned
/// movie, a rented movie with a rent line) are dropped, as after a payment.
/// </summary>
internal sealed class CommerceOwnerMergeHandler(AppDbContext db, TimeProvider time) : IOwnerMergeHandler
{
    public async Task MergeAsync(Guid fromOwnerId, Guid toOwnerId, CancellationToken cancellationToken)
    {
        await db.Set<Order>()
            .Where(o => o.OwnerId == fromOwnerId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.OwnerId, toOwnerId), cancellationToken);
        await db.Set<Entitlement>()
            .Where(e => e.OwnerId == fromOwnerId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.OwnerId, toOwnerId), cancellationToken);

        if (!await db.Set<Cart>().AnyAsync(c => c.OwnerId == fromOwnerId, cancellationToken))
        {
            return;
        }

        var now = time.GetUtcNow();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO commerce.carts (id, owner_id, updated_at)
            VALUES ({Guid.CreateVersion7(now)}, {toOwnerId}, {now})
            ON CONFLICT (owner_id) DO UPDATE SET updated_at = excluded.updated_at
            """,
            cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO commerce.cart_items AS target (cart_id, movie_id, offer_id, offer_type, added_at, added_by)
            SELECT user_cart.id, item.movie_id, item.offer_id, item.offer_type, item.added_at, item.added_by
            FROM commerce.cart_items item
            JOIN commerce.carts anonymous_cart ON anonymous_cart.id = item.cart_id AND anonymous_cart.owner_id = {fromOwnerId}
            JOIN commerce.carts user_cart ON user_cart.owner_id = {toOwnerId}
            ON CONFLICT (cart_id, movie_id) DO UPDATE
            SET offer_id = excluded.offer_id, offer_type = excluded.offer_type, added_at = excluded.added_at, added_by = excluded.added_by
            WHERE target.added_at < excluded.added_at
            """,
            cancellationToken);
        await db.Set<Cart>().Where(c => c.OwnerId == fromOwnerId).ExecuteDeleteAsync(cancellationToken);

        var items = await db.Set<CartItem>()
            .AsNoTracking()
            .Where(i => db.Set<Cart>().Any(c => c.Id == i.CartId && c.OwnerId == toOwnerId))
            .ToListAsync(cancellationToken);
        var active = await db.ActiveEntitlementsAsync(toOwnerId, [.. items.Select(i => i.MovieId)], now, cancellationToken);
        var blocked = items
            .Where(i => Entitlement.Blocks(i.OfferType, active[i.MovieId]) is not null)
            .Select(i => i.MovieId)
            .ToList();
        if (blocked.Count > 0)
        {
            await db.Set<CartItem>()
                .Where(i => blocked.Contains(i.MovieId) && db.Set<Cart>().Any(c => c.Id == i.CartId && c.OwnerId == toOwnerId))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
