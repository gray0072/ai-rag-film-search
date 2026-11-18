using System.ComponentModel;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using Microsoft.Extensions.Options;

namespace FilmSearch.AI.Assistant.Tools;

// Cart and checkout tools (spec 06 §3, FR-AST-5, FR-AST-6). They act on the cart of the current owner: the services
// take the owner from IOwnerContext, and no tool has an owner, cart or price argument. Every result shows the cart as
// the server computed it; the model never computes a price or a total.

/// <summary>Arguments of <c>add_to_cart</c>.</summary>
internal sealed record AddToCartArgs(
    [property: Description("Id of the movie, exactly as a tool returned it.")]
    Guid MovieId,
    [property: Description("buy (own forever) or rent (watch for a limited time). Use what the user asked for; if they did not say, ask them first.")]
    OfferType OfferType);

/// <summary>Arguments of <c>remove_from_cart</c>.</summary>
internal sealed record RemoveFromCartArgs(
    [property: Description("Id of the movie to remove, as view_cart or another tool returned it.")]
    Guid MovieId);

/// <summary><c>view_cart</c>: the cart with current prices and the total.</summary>
internal sealed class ViewCartTool(ICartService carts) : AssistantTool<NoArgs>
{
    public const string ToolName = "view_cart";

    public override string Name => ToolName;

    protected override string Description =>
        "Shows the user's cart: movies, buy or rent, current prices and the total. Use it when the user asks what is in the cart.";

    protected override async Task<ToolOutcome> ExecuteAsync(NoArgs arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var cart = await carts.GetAsync(context.Locale, cancellationToken);
        return ToolOutcome.Success(ToolResults.Cart(cart));
    }
}

/// <summary><c>add_to_cart</c>: adds the buy or rent offer of a movie (replaces the other offer of the same movie); emits <c>cart.updated</c>.</summary>
internal sealed class AddToCartTool(ICartService carts) : AssistantTool<AddToCartArgs>
{
    public const string ToolName = "add_to_cart";

    public override string Name => ToolName;

    protected override string Description =>
        "Adds a movie to the user's cart to buy or rent it. Use it only when the user clearly asks to buy, rent or add a movie. "
        + "Adding does not pay: to pay, call request_checkout.";

    protected override async Task<ToolOutcome> ExecuteAsync(AddToCartArgs arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.MovieId == Guid.Empty)
        {
            return ToolOutcome.Error($"`movieId` is required: {MovieIdHint}.");
        }

        CartView cart;
        try
        {
            cart = await carts.AddItemAsync(arguments.MovieId, arguments.OfferType, CartItemSource.Assistant, context.Locale, cancellationToken);
        }
        catch (CommerceException ex)
        {
            return ToolResults.CommerceError(ex);
        }

        var line = cart.Items.FirstOrDefault(i => i.MovieId == arguments.MovieId);
        var payload = new
        {
            added = line is null ? null : $"{line.Title}: {ToolResults.FormatOffer(new OfferItem(line.OfferId, line.OfferType, line.PriceMinor, line.Currency, line.RentalHours))}",
            cart = ToolResults.Cart(cart),
        };

        return ToolOutcome.Success(payload) with { Events = [new CartUpdatedEvent(cart)] };
    }
}

/// <summary><c>remove_from_cart</c>: removes a movie's line; emits <c>cart.updated</c> when something was removed.</summary>
internal sealed class RemoveFromCartTool(ICartService carts) : AssistantTool<RemoveFromCartArgs>
{
    public const string ToolName = "remove_from_cart";

    public override string Name => ToolName;

    protected override string Description => "Removes a movie from the user's cart. Use it only when the user asks for it.";

    protected override async Task<ToolOutcome> ExecuteAsync(RemoveFromCartArgs arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var before = await carts.GetAsync(context.Locale, cancellationToken);
        if (before.Items.All(i => i.MovieId != arguments.MovieId))
        {
            return ToolOutcome.Success(new { removed = (string?)null, notes = "This movie is not in the cart.", cart = ToolResults.Cart(before) });
        }

        var cart = await carts.RemoveItemAsync(arguments.MovieId, context.Locale, cancellationToken);
        var removed = before.Items.First(i => i.MovieId == arguments.MovieId);
        return ToolOutcome.Success(new { removed = removed.Title, cart = ToolResults.Cart(cart) }) with { Events = [new CartUpdatedEvent(cart)] };
    }
}

/// <summary>
/// <c>request_checkout</c> (FR-AST-6): asks the user to confirm the payment of the current cart. It only stores a pending
/// confirmation and emits <c>confirmation.required</c>; the order and the payment session are created by the
/// confirmations endpoint after the user's click. No tool can create a payment.
/// </summary>
internal sealed class RequestCheckoutTool(ICheckoutConfirmations confirmations, IOptions<AssistantOptions> options) : AssistantTool<NoArgs>
{
    public const string ToolName = "request_checkout";

    public const string AwaitingStatus = "awaiting_user_confirmation";

    public override string Name => ToolName;

    protected override string Description =>
        "Asks the user to confirm the payment of everything in the cart: the user sees the order and a Go to payment button. "
        + "Call it when the user wants to pay, check out or buy now, after the movies are in the cart. It does not pay: only the user's click does.";

    protected override async Task<ToolOutcome> ExecuteAsync(NoArgs arguments, ToolContext context, CancellationToken cancellationToken)
    {
        ConfirmationView confirmation;
        try
        {
            confirmation = await confirmations.RequestAsync(
                context.ConversationId, context.AnswerId, TimeSpan.FromMinutes(options.Value.ConfirmationTtlMinutes), context.Locale, cancellationToken);
        }
        catch (CommerceException ex)
        {
            return ToolResults.CommerceError(ex);
        }
        catch (ConfirmationException ex)
        {
            return ToolOutcome.Error(ex.Message);
        }

        var summary = confirmation.Summary;
        var payload = new
        {
            status = AwaitingStatus,
            items = summary.Items.Select(i => $"{i.Title}: {ToolResults.FormatOffer(new OfferItem(i.OfferId, i.OfferType, i.PriceMinor, i.Currency, i.RentalHours))}"),
            total = ToolResults.FormatMoney(summary.TotalMinor, summary.Currency),
            notes = "Nothing is paid yet. The user now sees the order with a Go to payment button. "
                + "Tell them to check the order and press the button; do not say that the payment is done.",
        };

        return ToolOutcome.Success(payload) with
        {
            Events = [new ConfirmationRequiredEvent(confirmation.Id, confirmation.Kind, summary, confirmation.ExpiresAt)],
        };
    }
}
