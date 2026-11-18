namespace FilmSearch.Core.Commerce;

/// <summary>
/// A payment provider with a hosted checkout page and signed webhooks (spec 07 §1, ADR-0004): <c>Fake</c> (I09) and
/// <c>Stripe</c> (I15). Order logic depends only on the normalized <see cref="PaymentWebhookEvent"/>.
/// </summary>
public interface IPaymentGateway
{
    PaymentProvider Provider { get; }

    /// <summary>Creates a hosted checkout session. The same <see cref="CreateCheckoutRequest.IdempotencyKey"/> returns the same session.</summary>
    Task<CheckoutSession> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>Closes an open session so it cannot be paid any more (a new attempt replaces it, or the order expired). No-op for a closed session.</summary>
    Task ExpireCheckoutSessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Verifies the signature of an incoming webhook and parses it.</summary>
    /// <exception cref="WebhookSignatureException">The signature is missing, invalid or too old.</exception>
    /// <exception cref="FormatException">The payload is not a valid event.</exception>
    PaymentWebhookEvent ParseWebhook(PaymentWebhookRequest request);
}

/// <param name="OrderId">Order id; sent back in the webhook metadata.</param>
/// <param name="Items">Lines shown on the hosted page.</param>
/// <param name="AmountMinor">Total in minor units.</param>
/// <param name="Currency">ISO 4217 currency.</param>
/// <param name="SuccessUrl">Where the provider sends the browser after a payment (the page polls the order; it never marks it paid).</param>
/// <param name="CancelUrl">Where the provider sends the browser when the user cancels.</param>
/// <param name="IdempotencyKey"><c>order:{orderId}:attempt:{n}</c>.</param>
/// <param name="ExpiresAt">The session cannot be paid after this time.</param>
public sealed record CreateCheckoutRequest(
    Guid OrderId,
    IReadOnlyList<CheckoutLineItem> Items,
    long AmountMinor,
    string Currency,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    DateTimeOffset ExpiresAt);

public sealed record CheckoutLineItem(string Title, long AmountMinor, string Currency);

public sealed record CheckoutSession(string SessionId, string CheckoutUrl, DateTimeOffset ExpiresAt);

/// <summary>A raw webhook delivery: the exact body (signatures cover the bytes) and the request headers.</summary>
public sealed record PaymentWebhookRequest(string Body, IReadOnlyDictionary<string, string> Headers);

/// <param name="ProviderEventId">Unique id of the event at the provider (deduplication key).</param>
/// <param name="Type">Normalized event type.</param>
/// <param name="SessionId">Checkout session the event is about.</param>
/// <param name="OrderId">Order id from the session metadata.</param>
/// <param name="AmountMinor">Amount of the payment in minor units.</param>
/// <param name="Currency">ISO 4217 currency.</param>
/// <param name="PayloadJson">The raw payload, stored with the event.</param>
public sealed record PaymentWebhookEvent(
    string ProviderEventId,
    PaymentEventType Type,
    string SessionId,
    Guid OrderId,
    long AmountMinor,
    string Currency,
    string PayloadJson);

public sealed class WebhookSignatureException(string message) : Exception(message);

/// <summary>Processes verified webhook events (spec 07 §5), the same for every gateway.</summary>
public interface IPaymentWebhookProcessor
{
    /// <summary>Records the event (deduplicated by provider event id) and applies it to the payment and the order in one transaction.</summary>
    /// <exception cref="Exception">Processing failed; the error is stored with the event and the provider should retry.</exception>
    Task<WebhookProcessingResult> ProcessAsync(PaymentProvider provider, PaymentWebhookEvent webhookEvent, CancellationToken cancellationToken);
}

public enum WebhookProcessingResult
{
    /// <summary>The event changed the payment and / or the order.</summary>
    Applied,

    /// <summary>A known event that was already processed: nothing to do.</summary>
    Duplicate,

    /// <summary>Processed, but it changed nothing: a stale event (out of order), an unknown session, an amount mismatch.</summary>
    Ignored,
}
