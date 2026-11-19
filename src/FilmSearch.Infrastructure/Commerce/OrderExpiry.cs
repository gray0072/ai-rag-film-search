using FilmSearch.Core.Commerce;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FilmSearch.Infrastructure.Commerce;

/// <summary>
/// Expires unpaid orders past their expiry time (FR-COM-7, spec 07 §6) and closes their open checkout sessions.
/// An order paid meanwhile by a webhook makes the save fail on its row version; the next pass reloads it and skips it.
/// </summary>
public sealed partial class OrderExpiryJob(
    AppDbContext db,
    PaymentGateways gateways,
    TimeProvider time,
    CommerceMetrics metrics,
    ILogger<OrderExpiryJob> logger)
{
    private const int BatchSize = 100;
    private const int MaxPasses = 20;

    /// <summary>Runs until no due order is left (or a pass limit is hit). Returns the number of expired orders.</summary>
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var expired = 0;

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            db.ChangeTracker.Clear();
            var due = await db.Set<Order>()
                .Where(o => (o.Status == OrderStatus.PendingPayment || o.Status == OrderStatus.Failed) && o.ExpiresAt <= now)
                .OrderBy(o => o.ExpiresAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (due.Count == 0)
            {
                break;
            }

            var previous = due.ToDictionary(o => o.Id, o => o.Status);
            foreach (var order in due)
            {
                order.Expire(now);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A webhook changed one of the orders: reload the batch.
                continue;
            }

            foreach (var order in due)
            {
                metrics.RecordStatusChange(previous[order.Id], order.Status);
            }

            expired += due.Count;
            await CloseSessionsAsync([.. due.Select(o => o.Id)], now, cancellationToken);
            LogExpired(logger, due.Count);

            if (due.Count < BatchSize)
            {
                break;
            }
        }

        return expired;
    }

    private async Task CloseSessionsAsync(IReadOnlyList<Guid> orderIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var open = await db.Set<Payment>()
            .Where(p => orderIds.Contains(p.OrderId) && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Failed))
            .ToListAsync(cancellationToken);

        foreach (var payment in open)
        {
            try
            {
                if (gateways.Get(payment.Provider) is { } gateway)
                {
                    await gateway.ExpireCheckoutSessionAsync(payment.ProviderSessionId, cancellationToken);
                }

                payment.Apply(PaymentEventType.Expired, now);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best effort: an open session of an expired order is still handled by the webhook rules (late success).
                LogSessionCloseFailed(logger, ex, payment.Provider, payment.ProviderSessionId);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expired {Count} unpaid order(s)")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not close {Provider} session {SessionId} of an expired order")]
    private static partial void LogSessionCloseFailed(ILogger logger, Exception exception, PaymentProvider provider, string sessionId);
}

/// <summary>
/// Runs <see cref="OrderExpiryJob"/> every <see cref="CommerceOptions.ExpiryScanSeconds"/> in the Worker (I16; the API
/// ran it before). Several Worker replicas may run it at the same time: the row version keeps that safe. Without the
/// Fake gateway (it lives in the API process) the payment of an expired fake order is just marked expired; the fake
/// session expires by its own time anyway.
/// </summary>
internal sealed partial class OrderExpiryService(
    IServiceScopeFactory scopes,
    IOptions<CommerceOptions> options,
    TimeProvider time,
    ILogger<OrderExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = options.Value.ExpiryScanSeconds;
        if (seconds <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<OrderExpiryJob>().ExpireDueAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogPassFailed(logger, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Order expiry pass failed; the next pass retries")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
