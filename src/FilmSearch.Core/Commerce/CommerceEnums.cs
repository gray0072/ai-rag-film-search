namespace FilmSearch.Core.Commerce;

/// <summary>Who put a line into the cart.</summary>
public enum CartItemSource
{
    User = 0,
    Assistant = 1,
}

/// <summary>State of an order (spec 03 §2). Changes only through methods of <see cref="Order"/>.</summary>
public enum OrderStatus
{
    PendingPayment = 0,
    Paid = 1,
    Failed = 2,
    Expired = 3,
    Refunded = 4,
}

/// <summary>Where an order was created.</summary>
public enum OrderSource
{
    Web = 0,
    Assistant = 1,
}

public enum PaymentProvider
{
    Fake = 0,
    Stripe = 1,
}

/// <summary>State of one checkout session (one payment attempt) at the provider.</summary>
public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Expired = 3,
    Refunded = 4,
}

/// <summary>Normalized type of a provider webhook event; every gateway maps its own event types to these.</summary>
public enum PaymentEventType
{
    Succeeded = 0,
    Failed = 1,
    Expired = 2,
    Refunded = 3,
}

public enum EntitlementType
{
    /// <summary>Bought: watchable forever.</summary>
    Owned = 0,

    /// <summary>Rented: watchable until the end of the rental period.</summary>
    Rental = 1,
}
