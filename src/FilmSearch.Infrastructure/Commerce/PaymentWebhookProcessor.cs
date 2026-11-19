using FilmSearch.Core.Commerce;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FilmSearch.Infrastructure.Commerce;

/// <summary>
/// Webhook processing shared by all gateways (spec 07 §5), after the gateway has verified the signature:
/// <list type="number">
/// <item>The event is recorded with a unique (provider, provider event id): a delivery of a known event does not insert a row.</item>
/// <item>In one transaction the event row is locked (<c>FOR UPDATE</c>): concurrent deliveries of one event run one after
/// another and the later ones see <see cref="PaymentEvent.ProcessedAt"/> and stop (FR-COM-5).</item>
/// <item>The payment row is locked too, so all events of one checkout session are applied one at a time.
/// The payment and the order change state, entitlements are created, the paid movies leave the cart, and the event
/// is marked processed, all in that transaction. A concurrent change of the order (<c>xmin</c>) retries the whole step.</item>
/// <item>A processing error is stored with the event and rethrown, so the endpoint answers 500 and the provider retries.
/// An event that was recorded but not processed is processed again on the next delivery.</item>
/// </list>
/// </summary>
internal sealed partial class PaymentWebhookProcessor(
    AppDbContext db,
    TimeProvider time,
    CommerceMetrics metrics,
    ILogger<PaymentWebhookProcessor> logger) : IPaymentWebhookProcessor
{
    private const int MaxConcurrencyRetries = 5;
    private const int MaxErrorLength = 2000;

    public async Task<WebhookProcessingResult> ProcessAsync(PaymentProvider provider, PaymentWebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        var receivedAt = time.GetUtcNow();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO commerce.payment_events (id, provider, provider_event_id, type, payload_json, received_at)
            VALUES ({Guid.CreateVersion7(receivedAt)}, {provider.ToString()}, {webhookEvent.ProviderEventId}, {webhookEvent.Type.ToString()}, {webhookEvent.PayloadJson}::jsonb, {receivedAt})
            ON CONFLICT (provider, provider_event_id) DO NOTHING
            """,
            cancellationToken);

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            var result = await strategy.ExecuteAsync(ct => ProcessRecordedAsync(provider, webhookEvent, ct), cancellationToken);
            LogProcessed(logger, provider, webhookEvent.ProviderEventId, webhookEvent.Type, webhookEvent.OrderId, result);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, provider, webhookEvent.ProviderEventId);
            db.ChangeTracker.Clear();
            var error = ex.Message.Length > MaxErrorLength ? ex.Message[..MaxErrorLength] : ex.Message;
            await db.Set<PaymentEvent>()
                .Where(e => e.Provider == provider && e.ProviderEventId == webhookEvent.ProviderEventId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Error, error), CancellationToken.None);
            throw;
        }
    }

    private async Task<WebhookProcessingResult> ProcessRecordedAsync(PaymentProvider provider, PaymentWebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var recorded = (await db.Set<PaymentEvent>()
                .FromSql($"SELECT * FROM commerce.payment_events WHERE provider = {provider.ToString()} AND provider_event_id = {webhookEvent.ProviderEventId} FOR UPDATE")
                .ToListAsync(cancellationToken))
                .Single();

            if (recorded.ProcessedAt is not null)
            {
                return WebhookProcessingResult.Duplicate;
            }

            var now = time.GetUtcNow();
            var (result, note, change) = await ApplyAsync(provider, webhookEvent, now, cancellationToken);
            recorded.ProcessedAt = now;
            recorded.Error = note;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                if (change is var (from, to))
                {
                    metrics.RecordStatusChange(from, to);
                }

                return result;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                // The order changed meanwhile (another event, the expiry job, a new checkout): apply to the fresh state.
                await transaction.RollbackAsync(cancellationToken);
            }
        }
    }

    private async Task<(WebhookProcessingResult Result, string? Note, (OrderStatus From, OrderStatus To)? Change)> ApplyAsync(
        PaymentProvider provider,
        PaymentWebhookEvent webhookEvent,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Row lock: different events of one session (success and failure, or two success events) run one after
        // another, and each reads the payment and the order as the previous one left them.
        var payment = (await db.Set<Payment>()
            .FromSql($"SELECT * FROM commerce.payments WHERE provider = {provider.ToString()} AND provider_session_id = {webhookEvent.SessionId} FOR UPDATE")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (payment is null)
        {
            LogUnknownSession(logger, provider, webhookEvent.SessionId);
            return (WebhookProcessingResult.Ignored, "Unknown checkout session.", null);
        }

        var order = await db.Set<Order>()
            .Include(o => o.Items)
            .Include(o => o.Entitlements)
            .AsSplitQuery()
            .SingleAsync(o => o.Id == payment.OrderId, cancellationToken);
        if (webhookEvent.OrderId != order.Id)
        {
            LogOrderMismatch(logger, webhookEvent.SessionId, webhookEvent.OrderId, order.Id);
            return (WebhookProcessingResult.Ignored, $"Order id {webhookEvent.OrderId} does not match the session's order {order.Id}.", null);
        }

        // An event of an older attempt must not fail or expire the order while a newer attempt is open.
        var isLatestAttempt = !await db.Set<Payment>().AnyAsync(p => p.OrderId == order.Id && p.Attempt > payment.Attempt, cancellationToken);
        var previousStatus = order.Status;
        var paymentChanged = false;
        var orderChanged = false;

        switch (webhookEvent.Type)
        {
            case PaymentEventType.Succeeded:
                if (webhookEvent.AmountMinor != payment.AmountMinor || !string.Equals(webhookEvent.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    LogAmountMismatch(logger, order.Id, webhookEvent.AmountMinor, webhookEvent.Currency, payment.AmountMinor, payment.Currency);
                    return (WebhookProcessingResult.Ignored, $"Amount {webhookEvent.AmountMinor} {webhookEvent.Currency} does not match {payment.AmountMinor} {payment.Currency}; needs manual review.", null);
                }

                paymentChanged = payment.Apply(PaymentEventType.Succeeded, now);
                orderChanged = order.MarkPaid(now, () => Guid.CreateVersion7(now));
                if (orderChanged)
                {
                    if (previousStatus == OrderStatus.Expired)
                    {
                        LogLateSuccess(logger, order.Id);
                    }

                    await RemovePaidMoviesFromCartAsync(order, cancellationToken);
                }

                break;

            case PaymentEventType.Failed:
                paymentChanged = payment.Apply(PaymentEventType.Failed, now);
                orderChanged = isLatestAttempt && order.MarkFailed();
                break;

            case PaymentEventType.Expired:
                paymentChanged = payment.Apply(PaymentEventType.Expired, now);
                // The session is closed; the order itself expires only at its own expiry time (a new checkout is still possible).
                orderChanged = isLatestAttempt && now >= order.ExpiresAt && order.Expire(now);
                break;

            case PaymentEventType.Refunded:
                paymentChanged = payment.Apply(PaymentEventType.Refunded, now);
                orderChanged = order.MarkRefunded(now);
                break;

            default:
                throw new FormatException($"Unknown payment event type '{webhookEvent.Type}'.");
        }

        (OrderStatus, OrderStatus)? change = orderChanged ? (previousStatus, order.Status) : null;
        return paymentChanged || orderChanged
            ? (WebhookProcessingResult.Applied, null, change)
            : (WebhookProcessingResult.Ignored, $"No change: payment {payment.Status}, order {order.Status}.", null);
    }

    /// <summary>Bought and rented movies leave the owner's cart in the same transaction as the payment.</summary>
    private Task<int> RemovePaidMoviesFromCartAsync(Order order, CancellationToken cancellationToken)
    {
        var movieIds = order.Items.Select(i => i.MovieId).ToList();
        return db.Set<CartItem>()
            .Where(i => movieIds.Contains(i.MovieId) && db.Set<Cart>().Any(c => c.Id == i.CartId && c.OwnerId == order.OwnerId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook {Provider} {EventId} ({Type}) for order {OrderId}: {Result}")]
    private static partial void LogProcessed(ILogger logger, PaymentProvider provider, string eventId, PaymentEventType type, Guid orderId, WebhookProcessingResult result);

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook {Provider} {EventId} failed; the provider will retry")]
    private static partial void LogFailed(ILogger logger, Exception exception, PaymentProvider provider, string eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook for unknown {Provider} session {SessionId} acknowledged without a change")]
    private static partial void LogUnknownSession(ILogger logger, PaymentProvider provider, string sessionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook for session {SessionId} names order {EventOrderId}, but the session belongs to order {OrderId}")]
    private static partial void LogOrderMismatch(ILogger logger, string sessionId, Guid eventOrderId, Guid orderId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Payment for order {OrderId} is {AmountMinor} {Currency}, expected {ExpectedMinor} {ExpectedCurrency}: not marked paid, needs manual review")]
    private static partial void LogAmountMismatch(ILogger logger, Guid orderId, long amountMinor, string currency, long expectedMinor, string expectedCurrency);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Late payment success for expired order {OrderId}: marked paid, the money was taken")]
    private static partial void LogLateSuccess(ILogger logger, Guid orderId);
}
