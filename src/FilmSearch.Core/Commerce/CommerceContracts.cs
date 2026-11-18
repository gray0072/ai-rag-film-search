using FilmSearch.Core.Catalog;

namespace FilmSearch.Core.Commerce;

// Contracts of the Commerce module (spec 02 §5). The HTTP endpoints and, later, the assistant's tools (I12) use the
// same services. The owner always comes from IOwnerContext, never from arguments.

/// <summary>The cart of the current owner. Prices and titles come from the catalog at read time.</summary>
public interface ICartService
{
    Task<CartView> GetAsync(string locale, CancellationToken cancellationToken);

    /// <summary>Adds the movie's active offer of <paramref name="offerType"/>; replaces another offer of the same movie (FR-COM-1).</summary>
    /// <exception cref="CommerceException"><see cref="CommerceError.MovieNotFound"/>, <see cref="CommerceError.OfferNotAvailable"/>, <see cref="CommerceError.AlreadyOwned"/>, <see cref="CommerceError.AlreadyRented"/>.</exception>
    Task<CartView> AddItemAsync(Guid movieId, OfferType offerType, CartItemSource addedBy, string locale, CancellationToken cancellationToken);

    /// <summary>Removes the movie's line. Removing a movie that is not in the cart is not an error.</summary>
    Task<CartView> RemoveItemAsync(Guid movieId, string locale, CancellationToken cancellationToken);
}

/// <summary>Orders and payment sessions of the current owner (spec 07 §2).</summary>
public interface ICheckoutService
{
    /// <summary>
    /// Creates a <see cref="OrderStatus.PendingPayment"/> order from the cart with a price snapshot (FR-COM-2).
    /// A repeated <see cref="CreateOrderCommand.IdempotencyKey"/> returns the order created with it (<see cref="CreateOrderResult.Replayed"/>).
    /// </summary>
    /// <exception cref="CommerceException"><see cref="CommerceError.CartEmpty"/>, <see cref="CommerceError.CartHasUnavailableItems"/>, <see cref="CommerceError.AlreadyOwned"/>, <see cref="CommerceError.AlreadyRented"/>.</exception>
    Task<CreateOrderResult> CreateOrderAsync(CreateOrderCommand command, string locale, CancellationToken cancellationToken);

    /// <summary>Starts a checkout session, or returns the open one (idempotent per order while the session is valid).</summary>
    /// <exception cref="CommerceException"><see cref="CommerceError.OrderNotFound"/>, <see cref="CommerceError.OrderNotPayable"/>.</exception>
    Task<CheckoutStart> StartCheckoutAsync(Guid orderId, CancellationToken cancellationToken);

    Task<OrderView?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Orders of the owner, newest first.</summary>
    Task<IReadOnlyList<OrderView>> ListOrdersAsync(CancellationToken cancellationToken);
}

/// <summary>The movies the current owner may watch (FR-COM-6).</summary>
public interface ILibraryService
{
    /// <summary>Active entitlements, one per movie (owned wins over a rental), newest first. Texts in <paramref name="locale"/>.</summary>
    Task<IReadOnlyList<LibraryItem>> GetLibraryAsync(string locale, CancellationToken cancellationToken);
}

/// <param name="IdempotencyKey">Value of the <c>Idempotency-Key</c> header.</param>
/// <param name="Source">Where the order is created.</param>
/// <param name="ConversationId">The assistant conversation that created the order, if any.</param>
public sealed record CreateOrderCommand(string IdempotencyKey, OrderSource Source = OrderSource.Web, Guid? ConversationId = null);

/// <param name="Order">The order.</param>
/// <param name="Replayed">True when the idempotency key was seen before and the existing order is returned.</param>
public sealed record CreateOrderResult(OrderView Order, bool Replayed);

public static class CommerceLimits
{
    public const int IdempotencyKeyMaxLength = 200;
}

/// <summary>Why a commerce operation was refused. Endpoints map it to Problem Details; the assistant (I12) to a tool error.</summary>
public enum CommerceError
{
    MovieNotFound,
    OfferNotAvailable,
    AlreadyOwned,
    AlreadyRented,
    CartEmpty,
    CartHasUnavailableItems,
    OrderNotFound,
    OrderNotPayable,
}

public sealed class CommerceException(CommerceError error, string message) : Exception(message)
{
    public CommerceError Error { get; } = error;
}

// Read models. Single-line doc comments: they appear in the OpenAPI document (AGENTS.md).

/// <param name="Items">Cart lines, oldest first.</param>
/// <param name="TotalMinor">Sum of the available lines in minor units.</param>
/// <param name="Currency">ISO 4217 currency of the total.</param>
public sealed record CartView(IReadOnlyList<CartLine> Items, long TotalMinor, string Currency);

/// <param name="MovieId">Movie id.</param>
/// <param name="Title">Title in the request locale.</param>
/// <param name="Year">Release year.</param>
/// <param name="PosterPath">TMDB poster path.</param>
/// <param name="OfferId">The chosen offer.</param>
/// <param name="OfferType">Buy or rent.</param>
/// <param name="PriceMinor">Current price of the offer in minor units.</param>
/// <param name="Currency">ISO 4217 currency.</param>
/// <param name="RentalHours">Rental period in hours (rent only).</param>
/// <param name="AddedBy">Who added the line.</param>
/// <param name="AddedAt">When the line was added or changed.</param>
/// <param name="Available">False when the offer is no longer sold; such lines are not in the total and block the order.</param>
public sealed record CartLine(
    Guid MovieId,
    string Title,
    int? Year,
    string? PosterPath,
    Guid OfferId,
    OfferType OfferType,
    long PriceMinor,
    string Currency,
    int? RentalHours,
    CartItemSource AddedBy,
    DateTimeOffset AddedAt,
    bool Available);

/// <param name="Id">Order id.</param>
/// <param name="Status">Order status.</param>
/// <param name="TotalMinor">Total in minor units.</param>
/// <param name="Currency">ISO 4217 currency.</param>
/// <param name="CreatedAt">When the order was created.</param>
/// <param name="ExpiresAt">An unpaid order expires at this time.</param>
/// <param name="PaidAt">When the payment was confirmed by the provider webhook.</param>
/// <param name="Source">Where the order was created.</param>
/// <param name="Items">Order lines with the price snapshot.</param>
public sealed record OrderView(
    Guid Id,
    OrderStatus Status,
    long TotalMinor,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? PaidAt,
    OrderSource Source,
    IReadOnlyList<OrderLine> Items)
{
    public static OrderView From(Order order) => new(
        order.Id,
        order.Status,
        order.TotalMinor,
        order.Currency,
        order.CreatedAt,
        order.ExpiresAt,
        order.PaidAt,
        order.Source,
        [.. order.Items.Select(i => new OrderLine(i.MovieId, i.OfferId, i.OfferType, i.TitleSnapshot, i.PriceMinorSnapshot, i.RentalHours))]);
}

/// <param name="MovieId">Movie id.</param>
/// <param name="OfferId">The offer that was ordered.</param>
/// <param name="OfferType">Buy or rent.</param>
/// <param name="Title">Title when the order was created.</param>
/// <param name="PriceMinor">Price when the order was created, in minor units.</param>
/// <param name="RentalHours">Rental period in hours (rent only).</param>
public sealed record OrderLine(Guid MovieId, Guid OfferId, OfferType OfferType, string Title, long PriceMinor, int? RentalHours);

/// <param name="OrderId">Order id.</param>
/// <param name="CheckoutUrl">Hosted payment page to redirect the browser to.</param>
/// <param name="ExpiresAt">The session and the unpaid order expire at this time.</param>
public sealed record CheckoutStart(Guid OrderId, string CheckoutUrl, DateTimeOffset ExpiresAt);

/// <param name="MovieId">Movie id.</param>
/// <param name="Title">Title in the request locale.</param>
/// <param name="Year">Release year.</param>
/// <param name="PosterPath">TMDB poster path.</param>
/// <param name="Type">Owned or rental.</param>
/// <param name="ValidFrom">Start of the entitlement.</param>
/// <param name="ValidUntil">End of a rental; null for owned movies.</param>
/// <param name="OrderId">The order that granted it.</param>
public sealed record LibraryItem(
    Guid MovieId,
    string Title,
    int? Year,
    string? PosterPath,
    EntitlementType Type,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil,
    Guid OrderId);
