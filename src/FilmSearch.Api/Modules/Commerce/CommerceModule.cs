using FilmSearch.Api.Localization;
using FilmSearch.Api.Modules.Commerce.FakeGateway;
using FilmSearch.Api.OpenApi;
using FilmSearch.Core.Commerce;
using FilmSearch.Infrastructure.Commerce;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FilmSearch.Api.Modules.Commerce;

/// <summary>Commerce endpoints (spec 04 "Commerce", spec 07): cart, orders, checkout, library, and the fake payment provider.</summary>
public static class CommerceModule
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string IdempotentReplayedHeader = "Idempotent-Replayed";

    public static WebApplicationBuilder AddCommerceModule(this WebApplicationBuilder builder)
    {
        builder.Services.AddCommerceInfrastructure(builder.Configuration);

        // The fake provider (I09) runs inside the API and is meant for Development and tests only (ADR-0004).
        var provider = builder.Configuration.GetValue<PaymentProvider?>($"{PaymentOptions.SectionName}:{nameof(PaymentOptions.Provider)}") ?? PaymentProvider.Fake;
        if (provider == PaymentProvider.Fake && builder.Environment.IsProduction() && !BuildTimeOpenApi.IsGeneratingDocument)
        {
            throw new InvalidOperationException("The Fake payment gateway must not run in Production. Set Payments:Provider to Stripe (I15).");
        }

        builder.Services.AddOptions<FakePaymentOptions>()
            .Bind(builder.Configuration.GetSection(FakePaymentOptions.SectionName))
            .Validate(o => o.WebhookDelayMinMs >= 0 && o.WebhookDelayMaxMs >= o.WebhookDelayMinMs, "Payments:Fake: 0 <= WebhookDelayMinMs <= WebhookDelayMaxMs.")
            .Validate(o => o.DuplicateDeliveries is >= 0 and <= 10, "Payments:Fake:DuplicateDeliveries must be between 0 and 10.")
            .Validate(o => o.LateDeliveryDelayMs >= 0 && o.SignatureToleranceSeconds > 0, "Payments:Fake: LateDeliveryDelayMs >= 0, SignatureToleranceSeconds > 0.")
            .ValidateOnStart();
        builder.Services.AddSingleton<FakeWebhookSigner>();
        builder.Services.AddSingleton<FakeWebhookDispatcher>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<FakeWebhookDispatcher>());
        builder.Services.AddSingleton<FakePaymentGateway>();
        builder.Services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<FakePaymentGateway>());
        builder.Services.AddHttpClient(FakeWebhookDispatcher.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(FakeWebhookDispatcher.CreatePrimaryHandler);

        return builder;
    }

    public static IEndpointRouteBuilder MapCommerceEndpoints(this IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/api/cart").WithTags("Cart");
        cart.MapGet("/", GetCart)
            .WithName("GetCart")
            .WithSummary("The owner's cart with current prices from the catalog. Titles in the Accept-Language locale.");
        cart.MapPost("/items", AddCartItem)
            .WithName("AddCartItem")
            .WithSummary("Adds a movie's buy or rent offer; replaces another offer of the same movie.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        cart.MapDelete("/items/{movieId:guid}", RemoveCartItem)
            .WithName("RemoveCartItem")
            .WithSummary("Removes a movie from the cart.");

        var orders = app.MapGroup("/api/orders").WithTags("Orders");
        orders.MapPost("/", CreateOrder)
            .WithName("CreateOrder")
            .WithSummary("Creates a pending order from the cart with a price snapshot. Requires the Idempotency-Key header; a repeated key returns the same order.")
            .ProducesProblem(StatusCodes.Status409Conflict);
        orders.MapGet("/", ListOrders)
            .WithName("ListOrders")
            .WithSummary("The owner's orders, newest first.");
        orders.MapGet("/{id:guid}", GetOrder)
            .WithName("GetOrder")
            .WithSummary("Order details with status. The success page polls it until the webhook marks the order paid.");
        orders.MapPost("/{id:guid}/checkout", StartCheckout)
            .WithName("StartCheckout")
            .WithSummary("Creates a payment session and returns the hosted page URL. Idempotent while the session is open; after a failed payment it starts a new attempt.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/api/library", GetLibrary)
            .WithTags("Library")
            .WithName("GetLibrary")
            .WithSummary("Movies the owner bought or currently rents, one entry per movie. Titles in the Accept-Language locale.");

        app.MapFakeCheckoutEndpoints();

        return app;
    }

    private static async Task<Ok<CartView>> GetCart(ICartService carts, HttpContext httpContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await carts.GetAsync(httpContext.GetLocale(), cancellationToken));

    private static async Task<Results<Ok<CartView>, ProblemHttpResult>> AddCartItem(
        AddCartItemRequest request,
        ICartService carts,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await carts.AddItemAsync(request.MovieId!.Value, request.OfferType!.Value, CartItemSource.User, httpContext.GetLocale(), cancellationToken));
        }
        catch (CommerceException ex)
        {
            return CommerceProblems.From(ex);
        }
    }

    private static async Task<Ok<CartView>> RemoveCartItem(Guid movieId, ICartService carts, HttpContext httpContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await carts.RemoveItemAsync(movieId, httpContext.GetLocale(), cancellationToken));

    private static async Task<Results<Created<OrderView>, ValidationProblem, ProblemHttpResult>> CreateOrder(
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        ICheckoutService checkout,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > CommerceLimits.IdempotencyKeyMaxLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [IdempotencyKeyHeader] = [$"The {IdempotencyKeyHeader} header is required (1–{CommerceLimits.IdempotencyKeyMaxLength} characters), e.g. a new UUID per checkout click."],
            });
        }

        try
        {
            var result = await checkout.CreateOrderAsync(new CreateOrderCommand(idempotencyKey.Trim()), httpContext.GetLocale(), cancellationToken);
            if (result.Replayed)
            {
                httpContext.Response.Headers[IdempotentReplayedHeader] = "true";
            }

            return TypedResults.Created($"/api/orders/{result.Order.Id}", result.Order);
        }
        catch (CommerceException ex)
        {
            return CommerceProblems.From(ex);
        }
    }

    private static async Task<Ok<IReadOnlyList<OrderView>>> ListOrders(ICheckoutService checkout, CancellationToken cancellationToken) =>
        TypedResults.Ok(await checkout.ListOrdersAsync(cancellationToken));

    private static async Task<Results<Ok<OrderView>, NotFound>> GetOrder(Guid id, ICheckoutService checkout, CancellationToken cancellationToken)
    {
        var order = await checkout.GetOrderAsync(id, cancellationToken);
        return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
    }

    private static async Task<Results<Ok<CheckoutStart>, ProblemHttpResult>> StartCheckout(Guid id, ICheckoutService checkout, CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await checkout.StartCheckoutAsync(id, cancellationToken));
        }
        catch (CommerceException ex)
        {
            return CommerceProblems.From(ex);
        }
    }

    private static async Task<Ok<IReadOnlyList<LibraryItem>>> GetLibrary(ILibraryService library, HttpContext httpContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await library.GetLibraryAsync(httpContext.GetLocale(), cancellationToken));
}
