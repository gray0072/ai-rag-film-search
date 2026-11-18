using System.Text.Json;
using System.Text.Json.Serialization;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Commerce;
using FilmSearch.Core.Owners;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FilmSearch.Infrastructure.Assistant;

/// <summary>
/// Checkout confirmations (FR-AST-6). The Commerce module is used only through <see cref="ICartService"/> and
/// <see cref="ICheckoutService"/>. State changes are single UPDATE statements: <c>StartCheckoutAsync</c> clears the
/// shared change tracker, and two clicks may race.
/// </summary>
internal sealed partial class CheckoutConfirmations(
    AppDbContext db,
    ICartService carts,
    ICheckoutService checkout,
    IOwnerContext owner,
    TimeProvider time,
    ILogger<CheckoutConfirmations> logger) : ICheckoutConfirmations
{
    private static readonly JsonSerializerOptions SummaryJson = CreateSummaryJson();

    public async Task<ConfirmationView> RequestAsync(Guid conversationId, Guid messageId, TimeSpan validFor, string locale, CancellationToken cancellationToken)
    {
        await EnsureConversationAsync(conversationId, cancellationToken);

        var cart = await carts.GetAsync(locale, cancellationToken);
        if (cart.Items.Count == 0)
        {
            throw new CommerceException(CommerceError.CartEmpty, "The cart is empty.");
        }

        if (cart.Items.Any(i => !i.Available))
        {
            throw new CommerceException(CommerceError.CartHasUnavailableItems, "Some movies in the cart are no longer sold. Remove them first.");
        }

        var now = time.GetUtcNow();
        await db.Set<CheckoutConfirmation>()
            .Where(c => c.ConversationId == conversationId && c.OwnerId == owner.OwnerId && c.Status == ConfirmationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ConfirmationStatus.Superseded).SetProperty(c => c.ResolvedAt, now), cancellationToken);

        var summary = new ConfirmationSummary(cart.Items, cart.TotalMinor, cart.Currency);
        var confirmation = new CheckoutConfirmation
        {
            Id = Guid.CreateVersion7(now),
            OwnerId = owner.OwnerId,
            ConversationId = conversationId,
            MessageId = messageId,
            Kind = ConfirmationKinds.Checkout,
            Status = ConfirmationStatus.Pending,
            CartHash = CartFingerprint.Compute(cart),
            SummaryJson = JsonSerializer.Serialize(summary, SummaryJson),
            CreatedAt = now,
            ExpiresAt = now + validFor,
        };
        db.Add(confirmation);
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(confirmation).State = EntityState.Detached;

        LogRequested(logger, confirmation.Id, conversationId, cart.Items.Count, cart.TotalMinor, cart.Currency);
        return ToView(confirmation, now);
    }

    public async Task<ConfirmationResult> ResolveAsync(Guid conversationId, Guid confirmationId, bool approved, string locale, CancellationToken cancellationToken)
    {
        var confirmation = await db.Set<CheckoutConfirmation>()
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == confirmationId && c.ConversationId == conversationId && c.OwnerId == owner.OwnerId, cancellationToken)
            ?? throw new ConfirmationException(ConfirmationError.ConfirmationNotFound, $"Confirmation {confirmationId} was not found.");

        var now = time.GetUtcNow();
        var status = confirmation.StatusAt(now);

        if (!approved)
        {
            return status switch
            {
                ConfirmationStatus.Declined => new ConfirmationResult(ConfirmationStatus.Declined, null, null, null),
                ConfirmationStatus.Pending or ConfirmationStatus.Expired => await DeclineAsync(confirmation, now, cancellationToken),
                _ => throw NotPending(status),
            };
        }

        // A second click (or a reload of the page) returns the session of the order created by the first one.
        if (status == ConfirmationStatus.Approved && confirmation.OrderId is { } existingOrder)
        {
            var again = await checkout.StartCheckoutAsync(existingOrder, cancellationToken);
            return new ConfirmationResult(ConfirmationStatus.Approved, again.OrderId, again.CheckoutUrl, again.ExpiresAt);
        }

        if (status == ConfirmationStatus.Expired)
        {
            throw new ConfirmationException(ConfirmationError.ConfirmationExpired, "The confirmation has expired. Ask the assistant to start the checkout again.");
        }

        if (status != ConfirmationStatus.Pending)
        {
            throw NotPending(status);
        }

        var cart = await carts.GetAsync(locale, cancellationToken);
        if (CartFingerprint.Compute(cart) != confirmation.CartHash)
        {
            LogCartChanged(logger, confirmation.Id);
            throw new ConfirmationException(ConfirmationError.CartChanged, "The cart has changed since the assistant asked. Ask the assistant to start the checkout again.");
        }

        // The key of the confirmation makes concurrent clicks create one order (spec 04 "Idempotency-Key").
        var order = await checkout.CreateOrderAsync(
            new CreateOrderCommand($"confirmation:{confirmation.Id:N}", OrderSource.Assistant, conversationId), locale, cancellationToken);
        var session = await checkout.StartCheckoutAsync(order.Order.Id, cancellationToken);

        var resolvedAt = time.GetUtcNow();
        await db.Set<CheckoutConfirmation>()
            .Where(c => c.Id == confirmation.Id && (c.Status == ConfirmationStatus.Pending || c.Status == ConfirmationStatus.Approved))
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.Status, ConfirmationStatus.Approved)
                    .SetProperty(c => c.OrderId, order.Order.Id)
                    .SetProperty(c => c.ResolvedAt, resolvedAt),
                cancellationToken);

        LogApproved(logger, confirmation.Id, order.Order.Id);
        return new ConfirmationResult(ConfirmationStatus.Approved, session.OrderId, session.CheckoutUrl, session.ExpiresAt);
    }

    public async Task<IReadOnlyList<ConfirmationView>> ListAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var confirmations = await db.Set<CheckoutConfirmation>()
            .AsNoTracking()
            .Where(c => c.ConversationId == conversationId && c.OwnerId == owner.OwnerId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var now = time.GetUtcNow();
        return [.. confirmations.Select(c => ToView(c, now))];
    }

    private async Task<ConfirmationResult> DeclineAsync(CheckoutConfirmation confirmation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var updated = await db.Set<CheckoutConfirmation>()
            .Where(c => c.Id == confirmation.Id && (c.Status == ConfirmationStatus.Pending || c.Status == ConfirmationStatus.Declined))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ConfirmationStatus.Declined).SetProperty(c => c.ResolvedAt, now), cancellationToken);
        if (updated == 0)
        {
            // Approved or superseded by a concurrent request in the meantime.
            throw NotPending(ConfirmationStatus.Approved);
        }

        LogDeclined(logger, confirmation.Id);
        return new ConfirmationResult(ConfirmationStatus.Declined, null, null, null);
    }

    private async Task EnsureConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (!await db.Set<Conversation>().AnyAsync(c => c.Id == conversationId && c.OwnerId == owner.OwnerId, cancellationToken))
        {
            throw new ConfirmationException(ConfirmationError.ConfirmationNotFound, $"Conversation {conversationId} was not found.");
        }
    }

    private static ConfirmationException NotPending(ConfirmationStatus status) =>
        new(ConfirmationError.ConfirmationNotPending, $"The confirmation is {status.ToString().ToLowerInvariant()}; it cannot be answered any more.");

    private static ConfirmationView ToView(CheckoutConfirmation confirmation, DateTimeOffset now) => new(
        confirmation.Id,
        confirmation.MessageId,
        confirmation.Kind,
        confirmation.StatusAt(now),
        JsonSerializer.Deserialize<ConfirmationSummary>(confirmation.SummaryJson, SummaryJson)!,
        confirmation.ExpiresAt,
        confirmation.OrderId);

    private static JsonSerializerOptions CreateSummaryJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Confirmation {ConfirmationId} requested in conversation {ConversationId}: {Items} item(s), {TotalMinor} {Currency}")]
    private static partial void LogRequested(ILogger logger, Guid confirmationId, Guid conversationId, int items, long totalMinor, string currency);

    [LoggerMessage(Level = LogLevel.Information, Message = "Confirmation {ConfirmationId} approved: order {OrderId}")]
    private static partial void LogApproved(ILogger logger, Guid confirmationId, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Confirmation {ConfirmationId} declined")]
    private static partial void LogDeclined(ILogger logger, Guid confirmationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Confirmation {ConfirmationId} refused: the cart changed")]
    private static partial void LogCartChanged(ILogger logger, Guid confirmationId);
}
