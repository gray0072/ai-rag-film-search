using System.Text.Json;
using FilmSearch.Core.Commerce;

namespace FilmSearch.Api.Modules.Commerce.FakeGateway;

/// <summary>
/// A payment provider inside the API process (spec 07 §3, ADR-0004). It keeps its sessions in memory, like a real
/// provider keeps them on its side (a restart loses them), serves a hosted page (<see cref="FakeCheckoutEndpoints"/>)
/// and confirms payments only through delayed, signed webhooks (<see cref="FakeWebhookDispatcher"/>).
/// </summary>
public sealed class FakePaymentGateway(FakeWebhookSigner signer, FakeWebhookDispatcher dispatcher, TimeProvider time) : IPaymentGateway
{
    public const string CheckoutPath = "/fake-checkout";

    private readonly Lock _lock = new();
    private readonly Dictionary<string, FakeCheckoutSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sessionsByIdempotencyKey = new(StringComparer.Ordinal);

    public PaymentProvider Provider => PaymentProvider.Fake;

    public Task<CheckoutSession> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_sessionsByIdempotencyKey.TryGetValue(request.IdempotencyKey, out var id))
            {
                id = $"fake_cs_{Guid.NewGuid():N}";
                _sessions[id] = new FakeCheckoutSession(
                    id,
                    request.OrderId,
                    request.Items,
                    request.AmountMinor,
                    request.Currency,
                    request.SuccessUrl,
                    request.CancelUrl,
                    request.ExpiresAt,
                    FakeSessionState.Open,
                    Declines: 0);
                _sessionsByIdempotencyKey[request.IdempotencyKey] = id;
            }

            var session = _sessions[id];
            return Task.FromResult(new CheckoutSession(id, $"{CheckoutPath}/{id}", session.ExpiresAt));
        }
    }

    public Task ExpireCheckoutSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var session) && session.State == FakeSessionState.Open)
            {
                _sessions[sessionId] = session with { State = FakeSessionState.Expired };
            }
        }

        return Task.CompletedTask;
    }

    public ParsedWebhook ParseWebhook(PaymentWebhookRequest request)
    {
        signer.Verify(request.Body, request.Headers.GetValueOrDefault(FakeWebhookSigner.HeaderName));

        FakeWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<FakeWebhookPayload>(request.Body, FakeWebhookPayload.Json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The webhook body is not a valid fake event.", ex);
        }

        if (payload?.Data is null || string.IsNullOrEmpty(payload.Id) || string.IsNullOrEmpty(payload.Data.SessionId))
        {
            throw new FormatException("The webhook body is not a valid fake event.");
        }

        var webhookEvent = new PaymentWebhookEvent(
            payload.Id,
            FakeWebhookPayload.ParseType(payload.Type),
            payload.Data.SessionId,
            payload.Data.OrderId,
            payload.Data.AmountMinor,
            payload.Data.Currency,
            request.Body);
        return ParsedWebhook.Of(webhookEvent, payload.Type);
    }

    /// <summary>The session as the hosted page shows it; an open session past its expiry time shows as expired.</summary>
    public FakeCheckoutSession? GetSession(string sessionId)
    {
        lock (_lock)
        {
            return Find(sessionId);
        }
    }

    /// <summary>The "Pay" button: the session is paid and a success webhook is queued. Only an open session can be paid.</summary>
    public FakeCheckoutSession? Pay(string sessionId) => Act(sessionId, session =>
    {
        dispatcher.Send(Event(session, FakeWebhookPayload.Succeeded), isFailure: false);
        return session with { State = FakeSessionState.Paid };
    });

    /// <summary>The "Decline" button: a failure webhook is queued; the session stays open, so the user can still pay (as on Stripe's page).</summary>
    public FakeCheckoutSession? Decline(string sessionId) => Act(sessionId, session =>
    {
        dispatcher.Send(Event(session, FakeWebhookPayload.Failed), isFailure: true);
        return session with { Declines = session.Declines + 1 };
    });

    /// <summary>Applies <paramref name="action"/> to an open session. Returns the session after the action, or as it is when it was not open; null when unknown.</summary>
    private FakeCheckoutSession? Act(string sessionId, Func<FakeCheckoutSession, FakeCheckoutSession> action)
    {
        lock (_lock)
        {
            var session = Find(sessionId);
            if (session is null || session.State != FakeSessionState.Open)
            {
                return session;
            }

            return _sessions[sessionId] = action(session);
        }
    }

    /// <summary>Call under the lock.</summary>
    private FakeCheckoutSession? Find(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        return session.State == FakeSessionState.Open && time.GetUtcNow() >= session.ExpiresAt
            ? session with { State = FakeSessionState.Expired }
            : session;
    }

    private FakeWebhookPayload Event(FakeCheckoutSession session, string type) => new(
        $"evt_fake_{Guid.NewGuid():N}",
        type,
        time.GetUtcNow().ToUnixTimeSeconds(),
        new FakeWebhookData(session.Id, session.OrderId, session.AmountMinor, session.Currency));
}

public enum FakeSessionState
{
    Open,
    Paid,
    Expired,
}

public sealed record FakeCheckoutSession(
    string Id,
    Guid OrderId,
    IReadOnlyList<CheckoutLineItem> Items,
    long AmountMinor,
    string Currency,
    string SuccessUrl,
    string CancelUrl,
    DateTimeOffset ExpiresAt,
    FakeSessionState State,
    int Declines);

/// <summary>Body of a fake webhook, shaped like a Stripe event: <c>{ id, type, created, data: { … } }</c>.</summary>
public sealed record FakeWebhookPayload(string Id, string Type, long Created, FakeWebhookData Data)
{
    public const string Succeeded = "checkout.payment_succeeded";
    public const string Failed = "checkout.payment_failed";
    public const string Expired = "checkout.session_expired";
    public const string Refunded = "charge.refunded";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PaymentEventType ParseType(string type) => type switch
    {
        Succeeded => PaymentEventType.Succeeded,
        Failed => PaymentEventType.Failed,
        Expired => PaymentEventType.Expired,
        Refunded => PaymentEventType.Refunded,
        _ => throw new FormatException($"Unknown fake event type '{type}'."),
    };
}

public sealed record FakeWebhookData(string SessionId, Guid OrderId, long AmountMinor, string Currency);
