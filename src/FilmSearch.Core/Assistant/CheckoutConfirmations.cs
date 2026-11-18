using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FilmSearch.Core.Commerce;

namespace FilmSearch.Core.Assistant;

// Human in the loop for payments (FR-AST-6, spec 06 §3 and §5). The request_checkout tool only stores a pending
// confirmation with a fingerprint of the cart; an order and a payment session are created only when the user answers
// through POST /api/chat/conversations/{id}/confirmations/{confirmationId}. No tool can call ResolveAsync.

/// <summary>Confirmations of the current owner's assistant conversations (checkout).</summary>
public interface ICheckoutConfirmations
{
    /// <summary>
    /// Stores a pending checkout confirmation for the owner's current cart (summary and fingerprint). Older pending
    /// confirmations of the conversation are superseded, so only the newest button works.
    /// </summary>
    /// <param name="conversationId">A conversation of the current owner.</param>
    /// <param name="messageId">The answer the confirmation belongs to (the button is shown under it).</param>
    /// <param name="validFor">How long the user can confirm.</param>
    /// <param name="locale">Locale of the titles in the summary.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="CommerceException"><see cref="CommerceError.CartEmpty"/>, <see cref="CommerceError.CartHasUnavailableItems"/>.</exception>
    Task<ConfirmationView> RequestAsync(Guid conversationId, Guid messageId, TimeSpan validFor, string locale, CancellationToken cancellationToken);

    /// <summary>
    /// The user's answer. Approved: checks the owner, the state, the expiry time and that the cart is unchanged, then
    /// creates the order (idempotency key of the confirmation, source <see cref="OrderSource.Assistant"/>) and the
    /// payment session. Approving again returns the same session (double click).
    /// </summary>
    /// <exception cref="ConfirmationException">Not found (also another owner's), not pending, expired, cart changed.</exception>
    /// <exception cref="CommerceException">The order cannot be created or paid (e.g. <see cref="CommerceError.AlreadyOwned"/>).</exception>
    Task<ConfirmationResult> ResolveAsync(Guid conversationId, Guid confirmationId, bool approved, string locale, CancellationToken cancellationToken);

    /// <summary>Confirmations of a conversation of the current owner, oldest first.</summary>
    Task<IReadOnlyList<ConfirmationView>> ListAsync(Guid conversationId, CancellationToken cancellationToken);
}

/// <summary>Kinds of confirmation. Only checkout for now.</summary>
public static class ConfirmationKinds
{
    public const string Checkout = "checkout";
}

/// <summary>State of a confirmation. <see cref="Expired"/> is never stored: a pending confirmation past its expiry time is shown as expired.</summary>
public enum ConfirmationStatus
{
    Pending = 0,
    Approved = 1,
    Declined = 2,

    /// <summary>A newer confirmation was requested in the same conversation.</summary>
    Superseded = 3,

    Expired = 4,
}

/// <summary>A confirmation the assistant asked for (stored in <c>assistant.confirmations</c>).</summary>
public sealed class CheckoutConfirmation
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid ConversationId { get; set; }

    /// <summary>The answer the confirmation belongs to; no foreign key (the answer is stored at the end of the turn).</summary>
    public Guid MessageId { get; set; }

    public string Kind { get; set; } = ConfirmationKinds.Checkout;

    public ConfirmationStatus Status { get; set; }

    /// <summary><see cref="CartFingerprint"/> of the cart the user was shown.</summary>
    public required string CartHash { get; set; }

    /// <summary>What the user was shown: lines and total computed by the server (JSON of <see cref="ConfirmationSummary"/>).</summary>
    public required string SummaryJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>The order created on approval.</summary>
    public Guid? OrderId { get; set; }

    public ConfirmationStatus StatusAt(DateTimeOffset now) =>
        Status == ConfirmationStatus.Pending && now >= ExpiresAt ? ConfirmationStatus.Expired : Status;
}

/// <summary>
/// Fingerprint of a cart: movie, offer, price, currency and availability of every line, independent of line order.
/// A confirmation is valid only while the cart has the same fingerprint, so the user pays exactly what they were shown.
/// </summary>
public static class CartFingerprint
{
    public static string Compute(CartView cart)
    {
        var lines = cart.Items
            .OrderBy(i => i.MovieId)
            .Select(i => string.Join(
                ':',
                i.MovieId.ToString("N"),
                i.OfferId.ToString("N"),
                i.PriceMinor.ToString(CultureInfo.InvariantCulture),
                i.Currency,
                i.Available ? "1" : "0"));
        var text = $"{string.Join('|', lines)}#{cart.TotalMinor.ToString(CultureInfo.InvariantCulture)}{cart.Currency}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

/// <summary>Why a confirmation answer was refused.</summary>
public enum ConfirmationError
{
    /// <summary>No such confirmation in this conversation of the owner.</summary>
    ConfirmationNotFound,

    /// <summary>Already declined, superseded by a newer one, or approved and then declined.</summary>
    ConfirmationNotPending,

    ConfirmationExpired,

    /// <summary>The cart changed after the confirmation was requested; the assistant must ask again.</summary>
    CartChanged,
}

public sealed class ConfirmationException(ConfirmationError error, string message) : Exception(message)
{
    public ConfirmationError Error { get; } = error;
}

// Doc comments stay on one line: these types appear in the OpenAPI document (AGENTS.md).

/// <summary>A confirmation the assistant asked for.</summary>
/// <param name="Id">Confirmation id.</param>
/// <param name="MessageId">The answer the confirmation belongs to.</param>
/// <param name="Kind">What is confirmed: checkout.</param>
/// <param name="Status">pending, approved, declined, superseded (a newer one was requested) or expired.</param>
/// <param name="Summary">What the user confirms: cart lines and the total computed by the server.</param>
/// <param name="ExpiresAt">The user can confirm until this time.</param>
/// <param name="OrderId">The order created on approval.</param>
public sealed record ConfirmationView(
    Guid Id,
    Guid MessageId,
    string Kind,
    ConfirmationStatus Status,
    ConfirmationSummary Summary,
    DateTimeOffset ExpiresAt,
    Guid? OrderId);

/// <summary>Result of the user's answer to a confirmation.</summary>
/// <param name="Status">approved or declined.</param>
/// <param name="OrderId">The order created on approval.</param>
/// <param name="CheckoutUrl">Hosted payment page to redirect the browser to (approved only).</param>
/// <param name="ExpiresAt">The payment session and the unpaid order expire at this time (approved only).</param>
public sealed record ConfirmationResult(ConfirmationStatus Status, Guid? OrderId, string? CheckoutUrl, DateTimeOffset? ExpiresAt);
