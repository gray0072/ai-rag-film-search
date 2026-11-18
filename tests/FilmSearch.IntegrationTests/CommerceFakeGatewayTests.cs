using System.Net;
using FilmSearch.Api.Modules.Commerce.FakeGateway;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using FilmSearch.Infrastructure.Commerce;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FilmSearch.IntegrationTests.Infrastructure.CommerceFixtureBase;

namespace FilmSearch.IntegrationTests;

/// <summary>The fake provider's delivery switches (spec 07 §3): duplicates and out-of-order delivery through real HTTP calls.</summary>
public sealed class CommerceFakeGatewayTests(FakeGatewaySwitchesFixture fixture) : IClassFixture<FakeGatewaySwitchesFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_event_is_delivered_three_times_and_processed_once()
    {
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, fixture.MovieIds[0], OfferType.Buy);
        var order = await CreateOrderAsync(client);
        var checkout = await CheckoutAsync(client, order.Id);

        using var _ = await PressAsync(client, checkout.CheckoutUrl, "pay");

        var deliveries = await fixture.Factory.Webhooks.WaitForAsync(SessionId(checkout.CheckoutUrl), d => d.Count == 3);
        deliveries.Select(d => d.Payload.Id).Distinct().ShouldHaveSingleItem();
        deliveries.ShouldAllBe(d => d.StatusCode == (int)HttpStatusCode.OK);
        deliveries.Count(d => d.ResponseBody.Contains("\"applied\"")).ShouldBe(1);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Paid);
        (await fixture.QueryAsync(db => db.Set<Entitlement>().CountAsync(e => e.OrderId == order.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task A_failure_delivered_after_the_success_keeps_the_order_paid()
    {
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, fixture.MovieIds[1], OfferType.Rent);
        var order = await CreateOrderAsync(client);
        var checkout = await CheckoutAsync(client, order.Id);
        var sessionId = SessionId(checkout.CheckoutUrl);

        // Decline, then pay on the same page: the failure is held back, so the success arrives first.
        using var declined = await PressAsync(client, checkout.CheckoutUrl, "decline");
        using var paid = await PressAsync(client, checkout.CheckoutUrl, "pay");

        var deliveries = await fixture.Factory.Webhooks.WaitForAsync(sessionId, d => d.Count(x => x.Payload.Type == FakeWebhookPayload.Failed) == 3);
        deliveries[0].Payload.Type.ShouldBe(FakeWebhookPayload.Succeeded);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Paid);
        (await fixture.QueryAsync(db => db.Set<Payment>().SingleAsync(p => p.OrderId == order.Id))).Status.ShouldBe(PaymentStatus.Succeeded);
    }

    [Fact]
    public async Task Expiry_job_closes_the_session_of_an_expired_order()
    {
        // Real clock here: move the order's expiry time into the past instead.
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, fixture.MovieIds[2], OfferType.Buy);
        var order = await CreateOrderAsync(client);
        var checkout = await CheckoutAsync(client, order.Id);
        await fixture.QueryAsync(db => db.Set<Order>().Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<OrderExpiryJob>().ExpireDueAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);
        }

        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Expired);
        (await fixture.QueryAsync(db => db.Set<Payment>().SingleAsync(p => p.OrderId == order.Id))).Status.ShouldBe(PaymentStatus.Expired);
        (await client.GetStringAsync(checkout.CheckoutUrl, Ct)).ShouldContain("expired");
    }
}
