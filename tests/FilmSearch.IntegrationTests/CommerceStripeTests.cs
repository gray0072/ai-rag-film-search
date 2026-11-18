using System.Net;
using System.Net.Http.Json;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using FilmSearch.Infrastructure.Commerce;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FilmSearch.IntegrationTests.Infrastructure.CommerceFixtureBase;

namespace FilmSearch.IntegrationTests;

/// <summary>
/// Stripe test mode through HTTP (I15, FR-COM-3..5): the real gateway and webhook endpoint, Stripe's API answered by
/// <see cref="FakeStripeApi"/>, and webhooks signed with the endpoint secret as Stripe signs them.
/// </summary>
public sealed class CommerceStripeTests(StripeCommerceFixture fixture) : IClassFixture<StripeCommerceFixture>
{
    private const string Completed = "checkout.session.completed";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeStripeApi Stripe => fixture.Factory.StripeApi;

    [Fact]
    public async Task Checkout_opens_one_stripe_session_per_attempt_with_absolute_return_urls()
    {
        using var client = fixture.CreateClient();
        var (order, checkout) = await StartAsync(client, fixture.MovieIds[0], fixture.MovieIds[1]);

        checkout.CheckoutUrl.ShouldStartWith("https://checkout.stripe.com/c/pay/cs_test_");
        var session = Stripe.Get(SessionId(checkout.CheckoutUrl)).ShouldNotBeNull();
        session.OrderId.ShouldBe(order.Id.ToString());
        session.AmountTotal.ShouldBe(order.TotalMinor);
        session.Currency.ShouldBe("usd");
        session.SuccessUrl.ShouldBe($"https://shop.example/checkout/success?orderId={order.Id}");
        session.ExpiresAt.ShouldBe(order.ExpiresAt.AddMinutes(30).ToUnixTimeSeconds());

        // While the session is open, checkout returns it again without calling Stripe.
        var creates = Stripe.Calls.Count(c => c == "POST /v1/checkout/sessions");
        (await CheckoutAsync(client, order.Id)).CheckoutUrl.ShouldBe(checkout.CheckoutUrl);
        Stripe.Calls.Count(c => c == "POST /v1/checkout/sessions").ShouldBe(creates);

        var payment = await fixture.QueryAsync(db => db.Set<Payment>().SingleAsync(p => p.OrderId == order.Id));
        payment.Provider.ShouldBe(PaymentProvider.Stripe);
        payment.Status.ShouldBe(PaymentStatus.Pending);
    }

    [Fact]
    public async Task A_completed_session_pays_the_order_once_however_often_stripe_delivers_it()
    {
        using var client = fixture.CreateClient();
        var (order, checkout) = await StartAsync(client, fixture.MovieIds[0]);
        var sessionId = SessionId(checkout.CheckoutUrl);
        Stripe.Complete(sessionId);
        var eventId = $"evt_test_{Guid.NewGuid():N}";

        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.PendingPayment);
        var first = await ReadAckAsync(await fixture.PostStripeEventAsync(Completed, sessionId, order.Id, order.TotalMinor, eventId: eventId));
        first.Result.ShouldBe(WebhookProcessingResult.Applied);

        // `stripe events resend`: the same event id again, also concurrently.
        var replays = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => fixture.PostStripeEventAsync(Completed, sessionId, order.Id, order.TotalMinor, eventId: eventId)));
        foreach (var replay in replays)
        {
            (await ReadAckAsync(replay)).Result.ShouldBe(WebhookProcessingResult.Duplicate);
        }

        var paid = await GetOrderAsync(client, order.Id);
        paid.Status.ShouldBe(OrderStatus.Paid);
        (await fixture.QueryAsync(db => db.Set<Entitlement>().CountAsync(e => e.OrderId == order.Id, Ct))).ShouldBe(1);
        (await fixture.QueryAsync(db => db.Set<PaymentEvent>().CountAsync(e => e.ProviderEventId == eventId, Ct))).ShouldBe(1);
        var library = (await client.GetFromJsonAsync<List<LibraryItem>>("/api/library", Json, Ct))!;
        library.ShouldHaveSingleItem().MovieId.ShouldBe(fixture.MovieIds[0]);
        (await client.GetFromJsonAsync<CartView>("/api/cart", Json, Ct))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Webhooks_with_a_bad_or_old_signature_are_rejected_and_not_stored()
    {
        using var client = fixture.CreateClient();
        var (order, checkout) = await StartAsync(client, fixture.MovieIds[1]);
        var eventId = $"evt_test_{Guid.NewGuid():N}";
        var body = $$"""{"id":"{{eventId}}","object":"event","type":"checkout.session.completed","data":{"object":{"id":"{{SessionId(checkout.CheckoutUrl)}}","object":"checkout.session","metadata":{"orderId":"{{order.Id}}"},"amount_total":{{order.TotalMinor}},"currency":"usd","payment_status":"paid"} } }""";

        using var wrongSecret = await fixture.PostSignedStripeAsync(body, "whsec_attacker", DateTimeOffset.UtcNow);
        using var tooOld = await fixture.PostSignedStripeAsync(body, StripeCommerceFixture.WebhookSecret, DateTimeOffset.UtcNow.AddMinutes(-10));

        wrongSecret.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        tooOld.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.QueryAsync(db => db.Set<PaymentEvent>().CountAsync(e => e.ProviderEventId == eventId, Ct))).ShouldBe(0);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.PendingPayment);
    }

    [Fact]
    public async Task Events_the_shop_does_not_act_on_are_acknowledged_without_a_change()
    {
        using var client = fixture.CreateClient();
        var (order, checkout) = await StartAsync(client, fixture.MovieIds[2]);
        var sessionId = SessionId(checkout.CheckoutUrl);

        // A declined card: Stripe keeps the customer on its page; the order stays pending.
        var declinedId = $"evt_test_{Guid.NewGuid():N}";
        var declined = $$"""{"id":"{{declinedId}}","object":"event","type":"payment_intent.payment_failed","data":{"object":{"id":"pi_test_1","object":"payment_intent","metadata":{"orderId":"{{order.Id}}"} } } }""";
        (await ReadAckAsync(await fixture.PostSignedStripeAsync(declined, StripeCommerceFixture.WebhookSecret, DateTimeOffset.UtcNow)))
            .Result.ShouldBe(WebhookProcessingResult.Ignored);

        // A bank debit: the session completes unpaid, the money arrives later.
        (await ReadAckAsync(await fixture.PostStripeEventAsync(Completed, sessionId, order.Id, order.TotalMinor, paymentStatus: "unpaid")))
            .Result.ShouldBe(WebhookProcessingResult.Ignored);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.PendingPayment);
        (await fixture.QueryAsync(db => db.Set<PaymentEvent>().CountAsync(e => e.ProviderEventId == declinedId, Ct))).ShouldBe(0);

        (await ReadAckAsync(await fixture.PostStripeEventAsync("checkout.session.async_payment_succeeded", sessionId, order.Id, order.TotalMinor)))
            .Result.ShouldBe(WebhookProcessingResult.Applied);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public async Task A_failed_async_payment_fails_the_order_and_a_new_attempt_closes_the_old_session()
    {
        using var client = fixture.CreateClient();
        var (order, checkout) = await StartAsync(client, fixture.MovieIds[3]);
        var oldSession = SessionId(checkout.CheckoutUrl);

        (await ReadAckAsync(await fixture.PostStripeEventAsync("checkout.session.async_payment_failed", oldSession, order.Id, order.TotalMinor, paymentStatus: "unpaid")))
            .Result.ShouldBe(WebhookProcessingResult.Applied);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Failed);

        var retry = await CheckoutAsync(client, order.Id);

        var newSession = SessionId(retry.CheckoutUrl);
        newSession.ShouldNotBe(oldSession);
        Stripe.Get(oldSession)!.Status.ShouldBe("expired");
        Stripe.Get(newSession)!.Status.ShouldBe("open");
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.PendingPayment);

        // Stripe reports the closed session: nothing changes any more.
        (await ReadAckAsync(await fixture.PostStripeEventAsync("checkout.session.expired", oldSession, order.Id, order.TotalMinor, paymentStatus: "unpaid")))
            .Result.ShouldBe(WebhookProcessingResult.Ignored);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.PendingPayment);
    }

    [Fact]
    public async Task Order_expiry_closes_the_stripe_session_and_a_payment_made_before_still_counts()
    {
        using var client = fixture.CreateClient();
        var (order, checkout) = await StartAsync(client, fixture.MovieIds[1]);
        var sessionId = SessionId(checkout.CheckoutUrl);
        await fixture.QueryAsync(db => db.Set<Order>()
            .Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<OrderExpiryJob>().ExpireDueAsync(Ct)).ShouldBeGreaterThanOrEqualTo(1);
        }

        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Expired);
        Stripe.Get(sessionId)!.Status.ShouldBe("expired");
        Stripe.Calls.ShouldContain($"POST /v1/checkout/sessions/{sessionId}/expire");

        // The customer paid just before the session was closed: the money was taken, so the order is paid (spec 03 §2).
        (await ReadAckAsync(await fixture.PostStripeEventAsync(Completed, sessionId, order.Id, order.TotalMinor)))
            .Result.ShouldBe(WebhookProcessingResult.Applied);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Paid);
    }

    private static async Task<(OrderView Order, CheckoutStart Checkout)> StartAsync(HttpClient client, params Guid[] movies)
    {
        foreach (var movie in movies)
        {
            await AddToCartAsync(client, movie, OfferType.Buy);
        }

        var order = await CreateOrderAsync(client);
        return (order, await CheckoutAsync(client, order.Id));
    }
}
