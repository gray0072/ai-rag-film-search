using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using FilmSearch.Core.Owners;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FilmSearch.Infrastructure.Commerce;

/// <summary>
/// Orders and checkout sessions of the current owner (spec 07 §2). The order never becomes paid here: only the
/// webhook processor does that (FR-COM-4).
/// </summary>
internal sealed partial class CheckoutService(
    AppDbContext db,
    ICatalogReader catalog,
    IOwnerContext owner,
    PaymentGateways gateways,
    IOptions<CommerceOptions> options,
    TimeProvider time,
    ILogger<CheckoutService> logger) : ICheckoutService
{
    private const int MaxListedOrders = 100;

    public async Task<CreateOrderResult> CreateOrderAsync(CreateOrderCommand command, string locale, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        if (command.IdempotencyKey.Length > CommerceLimits.IdempotencyKeyMaxLength)
        {
            throw new ArgumentException($"The idempotency key must be at most {CommerceLimits.IdempotencyKeyMaxLength} characters.", nameof(command));
        }

        // Idempotency (spec 04): a stored key returns the order created with it, whatever the cart holds now.
        if (await FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken) is { } existing)
        {
            return new CreateOrderResult(OrderView.From(existing), Replayed: true);
        }

        var items = await db.Set<CartItem>()
            .AsNoTracking()
            .Where(i => db.Set<Cart>().Any(c => c.Id == i.CartId && c.OwnerId == owner.OwnerId))
            .OrderBy(i => i.AddedAt)
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            throw new CommerceException(CommerceError.CartEmpty, "The cart is empty.");
        }

        var movies = (await catalog.GetMoviesAsync([.. items.Select(i => i.MovieId)], locale, cancellationToken)).ToDictionary(m => m.Id);
        var now = time.GetUtcNow();
        var active = await db.ActiveEntitlementsAsync(owner.OwnerId, [.. items.Select(i => i.MovieId)], now, cancellationToken);

        var drafts = new List<OrderItemDraft>(items.Count);
        foreach (var item in items)
        {
            // Prices come from the catalog now, not from the cart (spec 03 §3: the snapshot is taken here).
            var offer = movies.GetValueOrDefault(item.MovieId)?.Offers.FirstOrDefault(o => o.Id == item.OfferId)
                ?? throw new CommerceException(CommerceError.CartHasUnavailableItems, "Some movies in the cart are no longer sold. Remove them and try again.");
            var movie = movies[item.MovieId];

            if (Entitlement.Blocks(offer.Type, active[item.MovieId]) is { } error)
            {
                throw CartService.EntitlementError(error, movie.Title);
            }

            drafts.Add(new OrderItemDraft(movie.Id, offer.Id, offer.Type, movie.Title, offer.PriceMinor, offer.Currency, offer.RentalHours));
        }

        if (drafts.Select(d => d.Currency).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            throw new CommerceException(CommerceError.CartHasUnavailableItems, "The cart mixes currencies.");
        }

        var order = Order.Create(
            Guid.CreateVersion7(now),
            owner.OwnerId,
            drafts,
            command.Source,
            command.ConversationId,
            command.IdempotencyKey,
            now,
            options.Value.PaymentTimeout);
        db.Add(order);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // A concurrent request with the same key won the race: answer with its order.
            db.ChangeTracker.Clear();
            var winner = await FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken) ?? throw new InvalidOperationException("Unique violation without an order for the key.", ex);
            return new CreateOrderResult(OrderView.From(winner), Replayed: true);
        }

        LogOrderCreated(logger, order.Id, order.Items.Count, order.TotalMinor, order.Currency);
        return new CreateOrderResult(OrderView.From(order), Replayed: false);
    }

    public async Task<CheckoutStart> StartCheckoutAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var gateway = gateways.Active;
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Row lock: concurrent checkout calls for one order run one after another, so they share one session.
            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM commerce.orders WHERE id = {orderId} AND owner_id = {owner.OwnerId} FOR UPDATE",
                ct);
            var order = await db.Set<Order>()
                .Include(o => o.Items)
                .SingleOrDefaultAsync(o => o.Id == orderId && o.OwnerId == owner.OwnerId, ct)
                ?? throw new CommerceException(CommerceError.OrderNotFound, $"Order {orderId} was not found.");

            var now = time.GetUtcNow();
            if (!order.CanStartCheckout(now))
            {
                if (order.Status is OrderStatus.PendingPayment or OrderStatus.Failed)
                {
                    // Past its expiry time but the job has not run yet.
                    order.Expire(now);
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }

                throw new CommerceException(CommerceError.OrderNotPayable, $"Order {orderId} is {StatusText(order.Status)} and cannot be paid.");
            }

            var payments = await db.Set<Payment>()
                .Where(p => p.OrderId == order.Id)
                .OrderBy(p => p.Attempt)
                .ToListAsync(ct);

            if (order.Status == OrderStatus.PendingPayment
                && payments.LastOrDefault() is { Status: PaymentStatus.Pending } open
                && open.Provider == gateway.Provider)
            {
                // Idempotent per order while the session is valid (spec 04).
                return new CheckoutStart(order.Id, open.CheckoutUrl, order.ExpiresAt);
            }

            order.RetryPayment(now, options.Value.PaymentTimeout);

            // A new attempt replaces the old sessions: close them, so an old page cannot be paid as well.
            foreach (var old in payments.Where(p => p.Status is PaymentStatus.Pending or PaymentStatus.Failed))
            {
                if (gateways.Get(old.Provider) is { } oldGateway)
                {
                    await oldGateway.ExpireCheckoutSessionAsync(old.ProviderSessionId, ct);
                }

                old.Apply(PaymentEventType.Expired, now);
            }

            var attempt = payments.Count + 1;
            var session = await gateway.CreateCheckoutSessionAsync(
                new CreateCheckoutRequest(
                    order.Id,
                    [.. order.Items.Select(i => new CheckoutLineItem(i.TitleSnapshot, i.PriceMinorSnapshot, order.Currency))],
                    order.TotalMinor,
                    order.Currency,
                    options.Value.SuccessUrl.Replace("{orderId}", order.Id.ToString(), StringComparison.Ordinal),
                    options.Value.CancelUrl.Replace("{orderId}", order.Id.ToString(), StringComparison.Ordinal),
                    $"order:{order.Id}:attempt:{attempt}",
                    order.ExpiresAt),
                ct);

            db.Add(new Payment
            {
                Id = Guid.CreateVersion7(now),
                OrderId = order.Id,
                Provider = gateway.Provider,
                Attempt = attempt,
                ProviderSessionId = session.SessionId,
                Status = PaymentStatus.Pending,
                AmountMinor = order.TotalMinor,
                Currency = order.Currency,
                CheckoutUrl = session.CheckoutUrl,
                CreatedAt = now,
                UpdatedAt = now,
            });

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            LogCheckoutStarted(logger, order.Id, attempt, gateway.Provider, session.SessionId);
            return new CheckoutStart(order.Id, session.CheckoutUrl, order.ExpiresAt);
        }, cancellationToken);
    }

    public async Task<OrderView?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Set<Order>()
            .AsNoTracking()
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Id == orderId && o.OwnerId == owner.OwnerId, cancellationToken);

        return order is null ? null : OrderView.From(order);
    }

    public async Task<IReadOnlyList<OrderView>> ListOrdersAsync(CancellationToken cancellationToken)
    {
        var orders = await db.Set<Order>()
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.OwnerId == owner.OwnerId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(MaxListedOrders)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return [.. orders.Select(OrderView.From)];
    }

    private Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
        db.Set<Order>()
            .AsNoTracking()
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.OwnerId == owner.OwnerId && o.IdempotencyKey == key, cancellationToken);

    private static string StatusText(OrderStatus status) => status switch
    {
        OrderStatus.Paid => "already paid",
        OrderStatus.Refunded => "refunded",
        _ => "expired",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} created: {ItemCount} item(s), {TotalMinor} {Currency}")]
    private static partial void LogOrderCreated(ILogger logger, Guid orderId, int itemCount, long totalMinor, string currency);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checkout for order {OrderId}, attempt {Attempt}: {Provider} session {SessionId}")]
    private static partial void LogCheckoutStarted(ILogger logger, Guid orderId, int attempt, PaymentProvider provider, string sessionId);
}
