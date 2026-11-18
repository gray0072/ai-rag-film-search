using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using FilmSearch.Core.Owners;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FilmSearch.Infrastructure.Commerce;

/// <summary>
/// The owner's cart. Lines store the chosen offer only; titles and prices are read from the catalog on every view
/// (<see cref="ICatalogReader"/>, no access to catalog tables), so the cart never shows a stale price.
/// </summary>
internal sealed class CartService(AppDbContext db, ICatalogReader catalog, IOwnerContext owner, TimeProvider time) : ICartService
{
    public async Task<CartView> GetAsync(string locale, CancellationToken cancellationToken)
    {
        var items = await db.Set<CartItem>()
            .AsNoTracking()
            .Where(i => db.Set<Cart>().Any(c => c.Id == i.CartId && c.OwnerId == owner.OwnerId))
            .OrderBy(i => i.AddedAt)
            .ToListAsync(cancellationToken);

        return await BuildViewAsync(items, locale, cancellationToken);
    }

    public async Task<CartView> AddItemAsync(Guid movieId, OfferType offerType, CartItemSource addedBy, string locale, CancellationToken cancellationToken)
    {
        var movie = (await catalog.GetMoviesAsync([movieId], locale, cancellationToken)).SingleOrDefault()
            ?? throw new CommerceException(CommerceError.MovieNotFound, $"Movie {movieId} was not found.");
        var offer = movie.Offers.FirstOrDefault(o => o.Type == offerType)
            ?? throw new CommerceException(CommerceError.OfferNotAvailable, $"'{movie.Title}' cannot be {(offerType == OfferType.Rent ? "rented" : "bought")} now.");

        var now = time.GetUtcNow();
        var active = await db.ActiveEntitlementsAsync(owner.OwnerId, [movieId], now, cancellationToken);
        if (Entitlement.Blocks(offerType, active[movieId]) is { } error)
        {
            throw EntitlementError(error, movie.Title);
        }

        var cartId = await EnsureCartAsync(now, cancellationToken);

        // Upsert: one line per movie (FR-COM-1); a concurrent add of the same movie cannot create a second line.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO commerce.cart_items (cart_id, movie_id, offer_id, offer_type, added_at, added_by)
            VALUES ({cartId}, {movieId}, {offer.Id}, {offerType.ToString()}, {now}, {addedBy.ToString()})
            ON CONFLICT (cart_id, movie_id) DO UPDATE
            SET offer_id = excluded.offer_id, offer_type = excluded.offer_type, added_at = excluded.added_at, added_by = excluded.added_by
            """,
            cancellationToken);

        return await GetAsync(locale, cancellationToken);
    }

    public async Task<CartView> RemoveItemAsync(Guid movieId, string locale, CancellationToken cancellationToken)
    {
        var removed = await db.Set<CartItem>()
            .Where(i => i.MovieId == movieId && db.Set<Cart>().Any(c => c.Id == i.CartId && c.OwnerId == owner.OwnerId))
            .ExecuteDeleteAsync(cancellationToken);

        if (removed > 0)
        {
            var now = time.GetUtcNow();
            await db.Set<Cart>()
                .Where(c => c.OwnerId == owner.OwnerId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, now), cancellationToken);
        }

        return await GetAsync(locale, cancellationToken);
    }

    internal static CommerceException EntitlementError(CommerceError error, string title) => error switch
    {
        CommerceError.AlreadyOwned => new CommerceException(error, $"'{title}' is already in your library."),
        _ => new CommerceException(error, $"'{title}' is already rented; it can be bought, not rented again."),
    };

    /// <summary>The owner's cart id; creates the cart on first use (race-free: unique owner id, upsert).</summary>
    private async Task<Guid> EnsureCartAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ids = await db.Database.SqlQuery<Guid>(
            $"""
            INSERT INTO commerce.carts (id, owner_id, updated_at)
            VALUES ({Guid.CreateVersion7(now)}, {owner.OwnerId}, {now})
            ON CONFLICT (owner_id) DO UPDATE SET updated_at = excluded.updated_at
            RETURNING id AS "Value"
            """).ToListAsync(cancellationToken);

        return ids.Single();
    }

    private async Task<CartView> BuildViewAsync(List<CartItem> items, string locale, CancellationToken cancellationToken)
    {
        var movies = (await catalog.GetMoviesAsync([.. items.Select(i => i.MovieId)], locale, cancellationToken))
            .ToDictionary(m => m.Id);

        var lines = new List<CartLine>(items.Count);
        foreach (var item in items)
        {
            if (!movies.TryGetValue(item.MovieId, out var movie))
            {
                continue;
            }

            // Read models contain active offers only: a missing offer is no longer sold.
            var offer = movie.Offers.FirstOrDefault(o => o.Id == item.OfferId);
            lines.Add(new CartLine(
                movie.Id,
                movie.Title,
                movie.Year,
                movie.PosterPath,
                item.OfferId,
                item.OfferType,
                offer?.PriceMinor ?? 0,
                offer?.Currency ?? PricingRule.Currency,
                offer?.RentalHours,
                item.AddedBy,
                item.AddedAt,
                Available: offer is not null));
        }

        var available = lines.Where(l => l.Available).ToList();
        return new CartView(
            lines,
            available.Sum(l => l.PriceMinor),
            available.Select(l => l.Currency).FirstOrDefault() ?? PricingRule.Currency);
    }
}
