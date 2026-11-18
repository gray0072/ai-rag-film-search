using FilmSearch.Core.Catalog;

namespace FilmSearch.Core.Commerce;

/// <summary>The cart of an owner (one per owner). Prices are not stored: the cart always shows the current offer from the catalog.</summary>
public sealed class Cart
{
    public Guid Id { get; set; }

    /// <summary>User id or anonymous session id (<see cref="Owners.IOwnerContext"/>).</summary>
    public Guid OwnerId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<CartItem> Items { get; set; } = [];
}

/// <summary>One line per movie (FR-COM-1): adding another offer of the same movie replaces the line.</summary>
public sealed class CartItem
{
    public Guid CartId { get; set; }

    public Guid MovieId { get; set; }

    public Guid OfferId { get; set; }

    /// <summary>Type of the chosen offer, kept with the line so a line whose offer is no longer sold can still be shown.</summary>
    public OfferType OfferType { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    public CartItemSource AddedBy { get; set; }
}

/// <summary>
/// One checkout session at a payment provider for an order (an attempt). A failed payment can be retried with a new
/// session; a session can also succeed after a failure (the user retried the card on the provider page).
/// </summary>
public sealed class Payment
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public PaymentProvider Provider { get; set; }

    /// <summary>Attempt number of the order, starting at 1. Part of the provider idempotency key.</summary>
    public int Attempt { get; set; }

    public required string ProviderSessionId { get; set; }

    public string? ProviderPaymentIntentId { get; set; }

    public PaymentStatus Status { get; set; }

    public long AmountMinor { get; set; }

    public required string Currency { get; set; }

    public required string CheckoutUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Applies a provider event. Returns false when the event does not change the state (duplicate or stale).</summary>
    public bool Apply(PaymentEventType type, DateTimeOffset now)
    {
        var next = (type, Status) switch
        {
            (PaymentEventType.Succeeded, PaymentStatus.Pending or PaymentStatus.Failed or PaymentStatus.Expired) => PaymentStatus.Succeeded,
            (PaymentEventType.Failed, PaymentStatus.Pending) => PaymentStatus.Failed,
            (PaymentEventType.Expired, PaymentStatus.Pending or PaymentStatus.Failed) => PaymentStatus.Expired,
            (PaymentEventType.Refunded, PaymentStatus.Succeeded) => PaymentStatus.Refunded,
            _ => Status,
        };

        if (next == Status)
        {
            return false;
        }

        Status = next;
        UpdatedAt = now;
        return true;
    }
}

/// <summary>
/// A webhook event as received from a provider. The unique (<see cref="Provider"/>, <see cref="ProviderEventId"/>)
/// deduplicates deliveries (FR-COM-5); <see cref="ProcessedAt"/> is set when processing finished.
/// </summary>
public sealed class PaymentEvent
{
    public Guid Id { get; set; }

    public PaymentProvider Provider { get; set; }

    public required string ProviderEventId { get; set; }

    public PaymentEventType Type { get; set; }

    public required string PayloadJson { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Why the last processing failed, or why the event was acknowledged without a change.</summary>
    public string? Error { get; set; }
}

/// <summary>The right of an owner to watch a movie, granted by a paid order (FR-COM-6).</summary>
public sealed class Entitlement
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid MovieId { get; set; }

    public Guid OrderId { get; set; }

    public EntitlementType Type { get; set; }

    public DateTimeOffset ValidFrom { get; set; }

    /// <summary>End of a rental; null for owned movies.</summary>
    public DateTimeOffset? ValidUntil { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && ValidFrom <= now && (ValidUntil is null || now < ValidUntil);

    /// <summary>Whether an offer of this kind may still be bought: nothing for an owned movie; buying (not renting) a rented movie is allowed (spec 03 §3).</summary>
    public static CommerceError? Blocks(OfferType offerType, IEnumerable<Entitlement> activeEntitlements)
    {
        var active = activeEntitlements.ToList();
        if (active.Any(e => e.Type == EntitlementType.Owned))
        {
            return CommerceError.AlreadyOwned;
        }

        return offerType == OfferType.Rent && active.Any(e => e.Type == EntitlementType.Rental)
            ? CommerceError.AlreadyRented
            : null;
    }
}
