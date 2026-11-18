using System.Net;
using System.Net.Http.Json;
using FilmSearch.Api.Modules.Commerce;
using FilmSearch.Api.Modules.Commerce.FakeGateway;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static FilmSearch.IntegrationTests.Infrastructure.CommerceFixtureBase;

namespace FilmSearch.IntegrationTests;

/// <summary>Cart, orders, checkout, webhooks and library through HTTP (I09, FR-COM-1..7).</summary>
public sealed class CommerceApiTests(CommerceApiFixture fixture) : IClassFixture<CommerceApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid MovieOne => fixture.MovieIds[0];

    private Guid MovieTwo => fixture.MovieIds[1];

    private Guid MovieThree => fixture.MovieIds[2];

    [Fact]
    public async Task Cart_keeps_one_line_per_movie_with_current_catalog_prices()
    {
        using var client = fixture.CreateClient();
        var movie = (await client.GetFromJsonAsync<MovieDetails>($"/api/movies/{MovieOne}", Json, Ct))!;

        await AddToCartAsync(client, MovieOne, OfferType.Buy);
        var cart = await AddToCartAsync(client, MovieOne, OfferType.Rent);

        var line = cart.Items.ShouldHaveSingleItem();
        line.OfferType.ShouldBe(OfferType.Rent);
        line.PriceMinor.ShouldBe(movie.Offers.Single(o => o.Type == OfferType.Rent).PriceMinor);
        line.Title.ShouldBe("Commerce Movie One");
        line.AddedBy.ShouldBe(CartItemSource.User);
        line.Available.ShouldBeTrue();

        cart = await AddToCartAsync(client, MovieTwo, OfferType.Buy);
        cart.Items.Select(i => i.MovieId).ShouldBe([MovieOne, MovieTwo]);
        cart.TotalMinor.ShouldBe(cart.Items.Sum(i => i.PriceMinor));
        cart.Currency.ShouldBe("USD");

        using var removed = await client.DeleteAsync($"/api/cart/items/{MovieOne}", Ct);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await removed.Content.ReadFromJsonAsync<CartView>(Json, Ct))!.Items.ShouldHaveSingleItem().MovieId.ShouldBe(MovieTwo);

        using var removedAgain = await client.DeleteAsync($"/api/cart/items/{MovieOne}", Ct);
        removedAgain.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Titles follow Accept-Language like the catalog.
        using var russian = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        russian.Headers.AcceptLanguage.ParseAdd("ru");
        using var russianResponse = await client.SendAsync(russian, Ct);
        (await russianResponse.Content.ReadFromJsonAsync<CartView>(Json, Ct))!.Items.Single().Title.ShouldBe("Commerce Movie Two (ru)");
    }

    [Fact]
    public async Task Unknown_movie_is_404_and_an_invalid_body_is_400()
    {
        using var client = fixture.CreateClient();

        using var unknown = await client.PostAsJsonAsync("/api/cart/items", new { movieId = Guid.NewGuid(), offerType = "buy" }, Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString().ShouldBe("movieNotFound");

        using var missing = await client.PostAsJsonAsync("/api/cart/items", new { movieId = MovieOne }, Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Order_needs_an_idempotency_key_and_a_non_empty_cart()
    {
        using var client = fixture.CreateClient();

        using var noKey = await PostOrderAsync(client, idempotencyKey: null);
        noKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var empty = await PostOrderAsync(client, Guid.NewGuid().ToString());
        empty.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await empty.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString().ShouldBe("cartEmpty");
    }

    [Fact]
    public async Task A_repeated_idempotency_key_returns_the_same_order()
    {
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, MovieOne, OfferType.Buy);
        var key = Guid.NewGuid().ToString();

        var first = await CreateOrderAsync(client, key);
        using var replay = await PostOrderAsync(client, key);

        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.Headers.GetValues(CommerceModule.IdempotentReplayedHeader).ShouldBe(["true"]);
        (await replay.Content.ReadFromJsonAsync<OrderView>(Json, Ct))!.Id.ShouldBe(first.Id);

        // Concurrent requests with one new key (a double click) create one order.
        var concurrentKey = Guid.NewGuid().ToString();
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostOrderAsync(client, concurrentKey)));
        var ids = new List<Guid>();
        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
            ids.Add((await response.Content.ReadFromJsonAsync<OrderView>(Json, Ct))!.Id);
            response.Dispose();
        }

        ids.Distinct().ShouldHaveSingleItem().ShouldNotBe(first.Id);
        (await fixture.QueryAsync(db => db.Set<Order>().CountAsync(o => o.IdempotencyKey == concurrentKey))).ShouldBe(1);
    }

    [Fact]
    public async Task Full_flow_cart_order_fake_page_webhook_library()
    {
        // Acceptance criterion of I09: the whole flow through HTTP.
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, MovieOne, OfferType.Buy);
        var cart = await AddToCartAsync(client, MovieTwo, OfferType.Rent);

        var order = await CreateOrderAsync(client);
        order.Status.ShouldBe(OrderStatus.PendingPayment);
        order.TotalMinor.ShouldBe(cart.TotalMinor);
        order.Items.Select(i => (i.MovieId, i.OfferType, i.PriceMinor)).ShouldBe(cart.Items.Select(i => (i.MovieId, i.OfferType, i.PriceMinor)));
        order.ExpiresAt.ShouldBe(order.CreatedAt.AddMinutes(30));

        var checkout = await CheckoutAsync(client, order.Id);
        checkout.CheckoutUrl.ShouldStartWith(FakePaymentGateway.CheckoutPath + "/fake_cs_");
        (await CheckoutAsync(client, order.Id)).CheckoutUrl.ShouldBe(checkout.CheckoutUrl);

        var page = await client.GetStringAsync(checkout.CheckoutUrl, Ct);
        page.ShouldContain("Commerce Movie One");
        page.ShouldContain("/pay");

        using var paid = await PressAsync(client, checkout.CheckoutUrl, "pay");
        paid.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        paid.Headers.Location!.ToString().ShouldBe($"/checkout/success?orderId={order.Id}");

        var paidOrder = await WaitForStatusAsync(client, order.Id, OrderStatus.Paid);
        paidOrder.PaidAt.ShouldNotBeNull();

        var library = (await client.GetFromJsonAsync<List<LibraryItem>>("/api/library", Json, Ct))!;
        library.Count.ShouldBe(2);
        library.Single(i => i.MovieId == MovieOne).ShouldSatisfyAllConditions(
            i => i.Type.ShouldBe(EntitlementType.Owned),
            i => i.ValidUntil.ShouldBeNull(),
            i => i.Title.ShouldBe("Commerce Movie One"));
        library.Single(i => i.MovieId == MovieTwo).ShouldSatisfyAllConditions(
            i => i.Type.ShouldBe(EntitlementType.Rental),
            i => i.ValidUntil.ShouldBe(paidOrder.PaidAt!.Value.AddHours(48)));

        // Paid movies leave the cart; owned movies cannot be bought again, rented ones only bought.
        (await client.GetFromJsonAsync<CartView>("/api/cart", Json, Ct))!.Items.ShouldBeEmpty();
        using var buyAgain = await client.PostAsJsonAsync("/api/cart/items", new { movieId = MovieOne, offerType = "buy" }, Ct);
        buyAgain.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await buyAgain.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString().ShouldBe("alreadyOwned");
        using var rentAgain = await client.PostAsJsonAsync("/api/cart/items", new { movieId = MovieTwo, offerType = "rent" }, Ct);
        rentAgain.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await AddToCartAsync(client, MovieTwo, OfferType.Buy);

        var orders = (await client.GetFromJsonAsync<List<OrderView>>("/api/orders", Json, Ct))!;
        orders.ShouldHaveSingleItem().Status.ShouldBe(OrderStatus.Paid);

        // Paid orders cannot be checked out again.
        using var again = await client.PostAsync($"/api/orders/{order.Id}/checkout", content: null, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString().ShouldBe("orderNotPayable");
    }

    [Fact]
    public async Task Declined_payment_fails_the_order_and_a_new_checkout_can_pay_it()
    {
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, MovieThree, OfferType.Buy);
        var order = await CreateOrderAsync(client);
        var first = await CheckoutAsync(client, order.Id);

        using var declined = await PressAsync(client, first.CheckoutUrl, "decline");
        declined.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        declined.Headers.Location!.ToString().ShouldEndWith("?declined=1");
        await WaitForStatusAsync(client, order.Id, OrderStatus.Failed);
        (await client.GetFromJsonAsync<List<LibraryItem>>("/api/library", Json, Ct))!.ShouldBeEmpty();

        var second = await CheckoutAsync(client, order.Id);
        second.CheckoutUrl.ShouldNotBe(first.CheckoutUrl);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.PendingPayment);

        // The old session is closed by the new attempt.
        (await client.GetStringAsync(first.CheckoutUrl, Ct)).ShouldContain("expired");
        using var oldPay = await PressAsync(client, first.CheckoutUrl, "pay");
        oldPay.Headers.Location!.ToString().ShouldBe(first.CheckoutUrl);

        using var paid = await PressAsync(client, second.CheckoutUrl, "pay");
        await WaitForStatusAsync(client, order.Id, OrderStatus.Paid);

        var payments = await fixture.QueryAsync(db => db.Set<Payment>().Where(p => p.OrderId == order.Id).OrderBy(p => p.Attempt).ToListAsync());
        payments.Select(p => (p.Attempt, p.Status)).ShouldBe([(1, PaymentStatus.Expired), (2, PaymentStatus.Succeeded)]);
    }

    [Fact]
    public async Task Cancel_returns_to_the_cancel_page_and_leaves_the_order_pending()
    {
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, MovieOne, OfferType.Rent);
        var order = await CreateOrderAsync(client);
        var checkout = await CheckoutAsync(client, order.Id);

        using var cancelled = await PressAsync(client, checkout.CheckoutUrl, "cancel");

        cancelled.Headers.Location!.ToString().ShouldBe($"/checkout/cancel?orderId={order.Id}");
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.PendingPayment);
        (await client.GetFromJsonAsync<CartView>("/api/cart", Json, Ct))!.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_same_webhook_delivered_three_times_creates_entitlements_once()
    {
        // Acceptance criterion of I09 (FR-COM-5).
        var (client, order, sessionId) = await StartCheckoutAsync(MovieOne, MovieTwo);
        using var disposeClient = client;
        var eventId = $"evt_test_{Guid.NewGuid():N}";

        var results = new List<WebhookProcessingResult>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await fixture.PostWebhookAsync(FakeWebhookPayload.Succeeded, sessionId, order.Id, order.TotalMinor, eventId);
            results.Add((await ReadAckAsync(response)).Result);
        }

        results.ShouldBe([WebhookProcessingResult.Applied, WebhookProcessingResult.Duplicate, WebhookProcessingResult.Duplicate]);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Paid);
        (await fixture.QueryAsync(db => db.Set<Entitlement>().CountAsync(e => e.OrderId == order.Id))).ShouldBe(2);
        var events = await fixture.QueryAsync(db => db.Set<PaymentEvent>().Where(e => e.ProviderEventId == eventId).ToListAsync());
        events.ShouldHaveSingleItem().ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Concurrent_webhooks_for_one_payment_grant_entitlements_once()
    {
        var (client, order, sessionId) = await StartCheckoutAsync(MovieOne, MovieTwo, MovieThree);
        using var disposeClient = client;
        var eventId = $"evt_test_{Guid.NewGuid():N}";

        // Ten deliveries of one event and five different success events of the same session, all at once.
        var deliveries = Enumerable.Range(0, 10).Select(_ => fixture.PostWebhookAsync(FakeWebhookPayload.Succeeded, sessionId, order.Id, order.TotalMinor, eventId))
            .Concat(Enumerable.Range(0, 5).Select(_ => fixture.PostWebhookAsync(FakeWebhookPayload.Succeeded, sessionId, order.Id, order.TotalMinor)));
        var responses = await Task.WhenAll(deliveries);

        var results = new List<WebhookProcessingResult>();
        foreach (var response in responses)
        {
            results.Add((await ReadAckAsync(response)).Result);
            response.Dispose();
        }

        results.Count(r => r == WebhookProcessingResult.Applied).ShouldBe(1);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Paid);
        (await fixture.QueryAsync(db => db.Set<Entitlement>().CountAsync(e => e.OrderId == order.Id))).ShouldBe(3);
        (await fixture.QueryAsync(db => db.Set<PaymentEvent>().CountAsync(e => e.ProviderEventId == eventId))).ShouldBe(1);
    }

    [Fact]
    public async Task A_failure_that_arrives_after_the_success_does_not_undo_the_payment()
    {
        var (client, order, sessionId) = await StartCheckoutAsync(MovieThree);
        using var disposeClient = client;

        using var success = await fixture.PostWebhookAsync(FakeWebhookPayload.Succeeded, sessionId, order.Id, order.TotalMinor);
        (await ReadAckAsync(success)).Result.ShouldBe(WebhookProcessingResult.Applied);
        using var lateFailure = await fixture.PostWebhookAsync(FakeWebhookPayload.Failed, sessionId, order.Id, order.TotalMinor);
        (await ReadAckAsync(lateFailure)).Result.ShouldBe(WebhookProcessingResult.Ignored);

        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Paid);
        (await fixture.QueryAsync(db => db.Set<Payment>().SingleAsync(p => p.OrderId == order.Id))).Status.ShouldBe(PaymentStatus.Succeeded);
    }

    [Fact]
    public async Task A_late_failure_of_an_old_attempt_does_not_fail_the_new_attempt()
    {
        var (client, order, firstSession) = await StartCheckoutAsync(MovieTwo);
        using var disposeClient = client;
        using var failure = await fixture.PostWebhookAsync(FakeWebhookPayload.Failed, firstSession, order.Id, order.TotalMinor);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Failed);

        await CheckoutAsync(client, order.Id);
        using var stale = await fixture.PostWebhookAsync(FakeWebhookPayload.Failed, firstSession, order.Id, order.TotalMinor);

        (await ReadAckAsync(stale)).Result.ShouldBe(WebhookProcessingResult.Ignored);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.PendingPayment);
    }

    [Fact]
    public async Task A_success_with_a_wrong_amount_is_not_accepted()
    {
        var (client, order, sessionId) = await StartCheckoutAsync(MovieOne);
        using var disposeClient = client;

        using var response = await fixture.PostWebhookAsync(FakeWebhookPayload.Succeeded, sessionId, order.Id, order.TotalMinor - 1);

        (await ReadAckAsync(response)).Result.ShouldBe(WebhookProcessingResult.Ignored);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.PendingPayment);
    }

    [Fact]
    public async Task An_invalid_signature_is_400_and_nothing_is_stored()
    {
        var (client, order, sessionId) = await StartCheckoutAsync(MovieTwo);
        using var disposeClient = client;
        var eventId = $"evt_test_{Guid.NewGuid():N}";
        var body = System.Text.Json.JsonSerializer.Serialize(
            new FakeWebhookPayload(eventId, FakeWebhookPayload.Succeeded, 0, new FakeWebhookData(sessionId, order.Id, order.TotalMinor, "USD")),
            FakeWebhookPayload.Json);

        using var unsigned = await fixture.PostRawWebhookAsync(body, signature: null);
        using var forged = await fixture.PostRawWebhookAsync(body, $"t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()},v1={new string('a', 64)}");

        unsigned.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.QueryAsync(db => db.Set<PaymentEvent>().AnyAsync(e => e.ProviderEventId == eventId))).ShouldBeFalse();
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.PendingPayment);
    }

    [Fact]
    public async Task A_webhook_for_an_unknown_session_is_acknowledged_without_a_change()
    {
        using var response = await fixture.PostWebhookAsync(FakeWebhookPayload.Succeeded, "fake_cs_unknown", Guid.NewGuid(), 100);

        (await ReadAckAsync(response)).Result.ShouldBe(WebhookProcessingResult.Ignored);
    }

    [Fact]
    public async Task Orders_and_library_are_visible_only_to_their_owner()
    {
        var (owner, order, _) = await StartCheckoutAsync(MovieOne);
        using var disposeOwner = owner;
        using var stranger = fixture.CreateClient();

        using var get = await stranger.GetAsync($"/api/orders/{order.Id}", Ct);
        using var checkout = await stranger.PostAsync($"/api/orders/{order.Id}/checkout", content: null, Ct);

        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        checkout.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetFromJsonAsync<List<OrderView>>("/api/orders", Json, Ct))!.ShouldBeEmpty();
        (await stranger.GetFromJsonAsync<CartView>("/api/cart", Json, Ct))!.Items.ShouldBeEmpty();
    }

    /// <summary>A new owner with the movies (bought) in an order and an open checkout session.</summary>
    private async Task<(HttpClient Client, OrderView Order, string SessionId)> StartCheckoutAsync(params Guid[] movieIds)
    {
        var client = fixture.CreateClient();
        foreach (var movieId in movieIds)
        {
            await AddToCartAsync(client, movieId, OfferType.Buy);
        }

        var order = await CreateOrderAsync(client);
        var checkout = await CheckoutAsync(client, order.Id);
        return (client, order, SessionId(checkout.CheckoutUrl));
    }
}
