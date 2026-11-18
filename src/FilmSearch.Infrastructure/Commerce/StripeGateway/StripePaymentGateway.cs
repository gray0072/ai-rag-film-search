using System.Globalization;
using System.Net;
using FilmSearch.Core.Commerce;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace FilmSearch.Infrastructure.Commerce.StripeGateway;

/// <summary>
/// Stripe Checkout in test mode (spec 07 §4, ADR-0004). The shop does not create Stripe products: every session has
/// its line items as <c>price_data</c> from the order's price snapshot. The order is confirmed only by signed webhooks.
/// The only Stripe SDK types of the solution live in this folder; the rest of the code sees <see cref="IPaymentGateway"/>.
/// </summary>
public sealed partial class StripePaymentGateway(
    IHttpClientFactory httpClients,
    IOptions<StripeOptions> options,
    TimeProvider time,
    ILogger<StripePaymentGateway> logger) : IPaymentGateway
{
    public const string HttpClientName = "Stripe";
    public const string SignatureHeader = "Stripe-Signature";
    public const string OrderIdMetadataKey = "orderId";

    /// <summary>Stripe accepts <c>expires_at</c> from 30 minutes to 24 hours after the session is created.</summary>
    public static readonly TimeSpan MinSessionLifetime = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan MaxSessionLifetime = TimeSpan.FromHours(24) - TimeSpan.FromMinutes(5);
    private const int MaxProductNameLength = 250;

    public PaymentProvider Provider => PaymentProvider.Stripe;

    public async Task<CheckoutSession> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken cancellationToken)
    {
        var orderId = request.OrderId.ToString();
        var sessionOptions = new SessionCreateOptions
        {
            Mode = "payment",
            ClientReferenceId = orderId,
            Metadata = new Dictionary<string, string> { [OrderIdMetadataKey] = orderId },
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                Metadata = new Dictionary<string, string> { [OrderIdMetadataKey] = orderId },
            },
            LineItems =
            [
                .. request.Items.Select(item => new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = item.Currency.ToLowerInvariant(),
                        UnitAmount = item.AmountMinor,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = item.Title.Length > MaxProductNameLength ? item.Title[..MaxProductNameLength] : item.Title,
                        },
                    },
                }),
            ],
            SuccessUrl = Absolute(request.SuccessUrl, nameof(request.SuccessUrl)),
            CancelUrl = Absolute(request.CancelUrl, nameof(request.CancelUrl)),
            ExpiresAt = SessionExpiresAt(request.ExpiresAt, time.GetUtcNow()).UtcDateTime,
        };

        // The same key (order:{id}:attempt:{n}) with the same parameters returns the same session for 24 hours, so a
        // repeated call after a failure on our side does not open a second page. Every parameter above is derived from
        // the order, never from the current time, because Stripe refuses a known key with different parameters.
        var session = await Sessions().CreateAsync(sessionOptions, new RequestOptions { IdempotencyKey = request.IdempotencyKey }, cancellationToken);
        LogSessionCreated(logger, session.Id, request.OrderId);

        return new CheckoutSession(session.Id, session.Url, new DateTimeOffset(DateTime.SpecifyKind(session.ExpiresAt, DateTimeKind.Utc)));
    }

    public async Task ExpireCheckoutSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var sessions = Sessions();
        try
        {
            await sessions.ExpireAsync(sessionId, cancellationToken: cancellationToken);
            LogSessionExpired(logger, sessionId);
        }
        catch (StripeException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // Test data of the account was deleted: nothing left to close.
            LogSessionNotFound(logger, sessionId);
        }
        catch (StripeException ex) when (ex.HttpStatusCode == HttpStatusCode.BadRequest)
        {
            // Stripe refuses to expire a session that is not open (complete or already expired): that is a no-op here.
            var session = await sessions.GetAsync(sessionId, cancellationToken: cancellationToken);
            if (session.Status == "open")
            {
                throw;
            }
        }
    }

    public ParsedWebhook ParseWebhook(PaymentWebhookRequest request)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.WebhookSecret))
        {
            throw new WebhookSignatureException("Stripe:WebhookSecret is not configured.");
        }

        if (!request.Headers.TryGetValue(SignatureHeader, out var signature) || string.IsNullOrWhiteSpace(signature))
        {
            throw new WebhookSignatureException($"The {SignatureHeader} header is missing.");
        }

        try
        {
            EventUtility.ValidateSignature(request.Body, signature, settings.WebhookSecret, settings.WebhookToleranceSeconds, time.GetUtcNow().ToUnixTimeSeconds());
        }
        catch (StripeException ex)
        {
            throw new WebhookSignatureException(ex.Message);
        }

        Event stripeEvent;
        try
        {
            // The account's API version may differ from the SDK's: the few fields read below are stable across versions.
            stripeEvent = EventUtility.ParseEvent(request.Body, throwOnApiVersionMismatch: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new FormatException("The webhook body is not a valid Stripe event.", ex);
        }

        if (string.IsNullOrEmpty(stripeEvent?.Id) || string.IsNullOrEmpty(stripeEvent.Type))
        {
            throw new FormatException("The webhook body is not a valid Stripe event.");
        }

        return Map(stripeEvent, request.Body);
    }

    /// <summary>
    /// Stripe event types → normalized events (spec 07 §4). A declined card does not produce a Checkout event: the
    /// customer stays on Stripe's page and can try another card, so the order simply stays pending.
    /// </summary>
    private static ParsedWebhook Map(Event stripeEvent, string payload)
    {
        PaymentEventType? type = stripeEvent.Type switch
        {
            EventTypes.CheckoutSessionCompleted => PaymentEventType.Succeeded,
            EventTypes.CheckoutSessionAsyncPaymentSucceeded => PaymentEventType.Succeeded,
            EventTypes.CheckoutSessionAsyncPaymentFailed => PaymentEventType.Failed,
            EventTypes.CheckoutSessionExpired => PaymentEventType.Expired,
            _ => null,
        };
        if (type is null)
        {
            return ParsedWebhook.Ignored(stripeEvent.Id, stripeEvent.Type, "The shop does not act on this event type.");
        }

        if (stripeEvent.Data?.Object is not Session session)
        {
            throw new FormatException($"A {stripeEvent.Type} event without a Checkout Session.");
        }

        // completed + unpaid: an asynchronous payment method (e.g. a bank debit) is still processing; async_payment_succeeded follows.
        if (stripeEvent.Type == EventTypes.CheckoutSessionCompleted && session.PaymentStatus is not ("paid" or "no_payment_required"))
        {
            return ParsedWebhook.Ignored(stripeEvent.Id, stripeEvent.Type, $"Payment status is '{session.PaymentStatus}': waiting for the asynchronous payment.");
        }

        var orderIdText = session.Metadata?.GetValueOrDefault(OrderIdMetadataKey) ?? session.ClientReferenceId;
        if (!Guid.TryParse(orderIdText, CultureInfo.InvariantCulture, out var orderId))
        {
            // Another integration of the same account, or `stripe trigger`: acknowledge, so Stripe does not retry for days.
            return ParsedWebhook.Ignored(stripeEvent.Id, stripeEvent.Type, "The session has no order id: not a FilmSearch checkout.");
        }

        var webhookEvent = new PaymentWebhookEvent(
            stripeEvent.Id,
            type.Value,
            session.Id,
            orderId,
            session.AmountTotal ?? 0,
            (session.Currency ?? string.Empty).ToUpperInvariant(),
            payload);
        return ParsedWebhook.Of(webhookEvent, stripeEvent.Type);
    }

    /// <summary>
    /// The session lives <see cref="MinSessionLifetime"/> longer than the order (Stripe's minimum lifetime is 30 minutes,
    /// while the order may have less left). The order's own expiry is enforced by the expiry job, which closes the session
    /// (spec 07 §6); a payment in between is a late success (spec 03 §2). Derived from the order's time, so a retry with
    /// the same idempotency key sends the same value.
    /// </summary>
    public static DateTimeOffset SessionExpiresAt(DateTimeOffset orderExpiresAt, DateTimeOffset now)
    {
        var expiresAt = orderExpiresAt + MinSessionLifetime;
        return expiresAt > now + MaxSessionLifetime ? now + MaxSessionLifetime : expiresAt;
    }

    private SessionService Sessions()
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("Stripe:SecretKey is not configured.");
        }

        // Retries come from the standard resilience handler of the named client (ServiceDefaults), so the SDK's own are off.
        var http = new SystemNetHttpClient(httpClients.CreateClient(HttpClientName), maxNetworkRetries: 0, appInfo: null, enableTelemetry: false);
        return new SessionService(new StripeClient(settings.SecretKey, httpClient: http));
    }

    private static string Absolute(string url, string name) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? url
            : throw new InvalidOperationException($"Stripe needs an absolute {name}; set Commerce:WebBaseUrl. Got '{url}'.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Stripe Checkout Session {SessionId} created for order {OrderId}")]
    private static partial void LogSessionCreated(ILogger logger, string sessionId, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stripe Checkout Session {SessionId} expired")]
    private static partial void LogSessionExpired(ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stripe Checkout Session {SessionId} was not found; nothing to expire")]
    private static partial void LogSessionNotFound(ILogger logger, string sessionId);
}
