using FilmSearch.Core.Catalog;

namespace FilmSearch.Core.Commerce;

/// <summary>
/// The order aggregate (spec 03 §2). Items are a price snapshot taken when the order is created and never change.
/// The status changes only through the methods below, which check the current state. Methods driven by webhooks
/// (<see cref="MarkPaid"/>, <see cref="MarkFailed"/>, <see cref="MarkRefunded"/>) return false for a duplicate or
/// stale event instead of throwing, because providers deliver events more than once and out of order.
/// </summary>
public sealed class Order
{
    /// <summary>Rental period when an offer does not say (spec 03: 48 hours).</summary>
    public const int DefaultRentalHours = 48;

    private readonly List<OrderItem> _items = [];
    private readonly List<Entitlement> _entitlements = [];

    private Order()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public long TotalMinor { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>An unpaid order expires at this time (FR-COM-7). Retrying a failed payment moves it.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public OrderSource Source { get; private set; }

    public Guid? ConversationId { get; private set; }

    /// <summary>The <c>Idempotency-Key</c> of the request that created the order; unique per owner.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Row version for optimistic concurrency (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;

    /// <summary>Entitlements granted by this order (created by <see cref="MarkPaid"/>).</summary>
    public IReadOnlyList<Entitlement> Entitlements => _entitlements;

    public static Order Create(
        Guid id,
        Guid ownerId,
        IReadOnlyList<OrderItemDraft> items,
        OrderSource source,
        Guid? conversationId,
        string? idempotencyKey,
        DateTimeOffset now,
        TimeSpan paymentTimeout)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("An order needs at least one item.", nameof(items));
        }

        if (items.Select(i => i.MovieId).Distinct().Count() != items.Count)
        {
            throw new ArgumentException("An order has one line per movie.", nameof(items));
        }

        if (items.Select(i => i.Currency).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            throw new ArgumentException("All items of an order must have the same currency.", nameof(items));
        }

        if (items.Any(i => i.PriceMinor < 0))
        {
            throw new ArgumentException("Prices must not be negative.", nameof(items));
        }

        if (paymentTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(paymentTimeout), "The payment timeout must be positive.");
        }

        var order = new Order
        {
            Id = id,
            OwnerId = ownerId,
            Status = OrderStatus.PendingPayment,
            TotalMinor = items.Sum(i => i.PriceMinor),
            Currency = items[0].Currency,
            CreatedAt = now,
            ExpiresAt = now + paymentTimeout,
            Source = source,
            ConversationId = conversationId,
            IdempotencyKey = idempotencyKey,
        };

        order._items.AddRange(items.Select(i => new OrderItem
        {
            OrderId = id,
            MovieId = i.MovieId,
            OfferId = i.OfferId,
            OfferType = i.OfferType,
            TitleSnapshot = i.Title,
            PriceMinorSnapshot = i.PriceMinor,
            RentalHours = i.OfferType == OfferType.Rent ? i.RentalHours ?? DefaultRentalHours : null,
        }));

        return order;
    }

    /// <summary>True while a checkout session may be started: pending or failed, and not past <see cref="ExpiresAt"/>.</summary>
    public bool CanStartCheckout(DateTimeOffset now) =>
        Status is OrderStatus.PendingPayment or OrderStatus.Failed && now < ExpiresAt;

    /// <summary>
    /// A confirmed payment: the order becomes <see cref="OrderStatus.Paid"/> and grants entitlements in the same change.
    /// Allowed from <c>PendingPayment</c>, <c>Failed</c> (a retry on the provider page succeeded) and <c>Expired</c>
    /// (a late success webhook: the money was taken, spec 03 §2). Returns false when the order is already paid or refunded.
    /// </summary>
    public bool MarkPaid(DateTimeOffset now, Func<Guid> newEntitlementId)
    {
        if (Status is OrderStatus.Paid or OrderStatus.Refunded)
        {
            return false;
        }

        Status = OrderStatus.Paid;
        PaidAt = now;

        foreach (var item in _items)
        {
            var rental = item.OfferType == OfferType.Rent;
            _entitlements.Add(new Entitlement
            {
                Id = newEntitlementId(),
                OwnerId = OwnerId,
                MovieId = item.MovieId,
                OrderId = Id,
                Type = rental ? EntitlementType.Rental : EntitlementType.Owned,
                ValidFrom = now,
                ValidUntil = rental ? now.AddHours(item.RentalHours ?? DefaultRentalHours) : null,
            });
        }

        return true;
    }

    /// <summary>A declined payment. Only a pending order becomes <c>Failed</c>; a stale failure after success or expiry is ignored (false).</summary>
    public bool MarkFailed()
    {
        if (Status != OrderStatus.PendingPayment)
        {
            return false;
        }

        Status = OrderStatus.Failed;
        return true;
    }

    /// <summary>
    /// The user starts a new checkout after a failed payment: <c>Failed → PendingPayment</c> with a new expiry time.
    /// No-op (false) for a pending order. Throws for paid, expired or refunded orders and after <see cref="ExpiresAt"/>.
    /// </summary>
    public bool RetryPayment(DateTimeOffset now, TimeSpan paymentTimeout)
    {
        if (!CanStartCheckout(now))
        {
            throw new OrderStateException(Id, Status, "start a payment");
        }

        if (Status == OrderStatus.PendingPayment)
        {
            return false;
        }

        Status = OrderStatus.PendingPayment;
        ExpiresAt = now + paymentTimeout;
        return true;
    }

    /// <summary>
    /// Expires an unpaid (pending or failed) order past <see cref="ExpiresAt"/>. Returns false for paid, refunded
    /// or already expired orders. Throws when the order is unpaid but not due yet.
    /// </summary>
    public bool Expire(DateTimeOffset now)
    {
        if (Status is not (OrderStatus.PendingPayment or OrderStatus.Failed))
        {
            return false;
        }

        if (now < ExpiresAt)
        {
            throw new OrderStateException(Id, Status, $"expire before {ExpiresAt:O}");
        }

        Status = OrderStatus.Expired;
        return true;
    }

    /// <summary>A refund (stretch, FR-COM-8): a paid order becomes <c>Refunded</c> and its entitlements are revoked. False for any other state.</summary>
    public bool MarkRefunded(DateTimeOffset now)
    {
        if (Status != OrderStatus.Paid)
        {
            return false;
        }

        Status = OrderStatus.Refunded;
        foreach (var entitlement in _entitlements.Where(e => e.RevokedAt is null))
        {
            entitlement.RevokedAt = now;
        }

        return true;
    }
}

/// <summary>A line of an order: the offer and its price at the time the order was created (never changes).</summary>
public sealed class OrderItem
{
    public Guid OrderId { get; init; }

    public Guid MovieId { get; init; }

    public Guid OfferId { get; init; }

    public OfferType OfferType { get; init; }

    public required string TitleSnapshot { get; init; }

    public long PriceMinorSnapshot { get; init; }

    public int? RentalHours { get; init; }
}

/// <summary>Input of <see cref="Order.Create"/>: one cart line with the current offer from the catalog.</summary>
public sealed record OrderItemDraft(
    Guid MovieId,
    Guid OfferId,
    OfferType OfferType,
    string Title,
    long PriceMinor,
    string Currency,
    int? RentalHours);

/// <summary>An operation is not allowed in the current state of the order.</summary>
public sealed class OrderStateException(Guid orderId, OrderStatus status, string operation)
    : InvalidOperationException($"Order {orderId} is {status}: cannot {operation}.")
{
    public Guid OrderId { get; } = orderId;

    public OrderStatus Status { get; } = status;
}
