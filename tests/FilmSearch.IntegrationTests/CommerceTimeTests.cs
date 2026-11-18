using System.Net;
using System.Net.Http.Json;
using FilmSearch.Api.Modules.Commerce.FakeGateway;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using FilmSearch.Infrastructure.Commerce;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using static FilmSearch.IntegrationTests.Infrastructure.CommerceFixtureBase;

namespace FilmSearch.IntegrationTests;

/// <summary>Webhook timing and order expiry on a fake clock (<see cref="CommerceTimeFixture"/>). Tests of this class run one after another.</summary>
public sealed class CommerceTimeTests(CommerceTimeFixture fixture) : IClassFixture<CommerceTimeFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Order_is_not_paid_before_the_webhook_arrives()
    {
        // Acceptance criterion of I09 (FR-COM-4): the success redirect does not mark the order paid.
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, fixture.MovieIds[0], OfferType.Buy);
        var order = await CreateOrderAsync(client);
        var checkout = await CheckoutAsync(client, order.Id);

        using var paid = await PressAsync(client, checkout.CheckoutUrl, "pay");
        paid.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.PendingPayment);
        (await client.GetStringAsync(checkout.CheckoutUrl, Ct)).ShouldContain("Paid.");

        await Task.Delay(300, Ct);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.PendingPayment);
        fixture.Factory.Webhooks.ForSession(SessionId(checkout.CheckoutUrl)).ShouldBeEmpty();

        fixture.Time.Advance(TimeSpan.FromSeconds(2));

        await WaitForStatusAsync(client, order.Id, OrderStatus.Paid);
    }

    [Fact]
    public async Task Unpaid_orders_expire_after_30_minutes_and_paid_ones_stay_paid()
    {
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, fixture.MovieIds[1], OfferType.Buy);
        var pending = await CreateOrderAsync(client);
        var checkout = await CheckoutAsync(client, pending.Id);

        using var other = fixture.CreateClient();
        await AddToCartAsync(other, fixture.MovieIds[2], OfferType.Buy);
        var paid = await CreateOrderAsync(other);
        var paidSession = SessionId((await CheckoutAsync(other, paid.Id)).CheckoutUrl);
        using (var success = await fixture.PostWebhookAsync(FakeWebhookPayload.Succeeded, paidSession, paid.Id, paid.TotalMinor))
        {
            (await ReadAckAsync(success)).Result.ShouldBe(WebhookProcessingResult.Applied);
        }

        fixture.Time.Advance(TimeSpan.FromMinutes(29));
        await ExpireDueAsync();
        (await GetOrderAsync(client, pending.Id)).Status.ShouldBe(OrderStatus.PendingPayment);

        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        await ExpireDueAsync();

        (await GetOrderAsync(client, pending.Id)).Status.ShouldBe(OrderStatus.Expired);
        (await GetOrderAsync(other, paid.Id)).Status.ShouldBe(OrderStatus.Paid);
        (await client.GetStringAsync(checkout.CheckoutUrl, Ct)).ShouldContain("expired");
        using var again = await client.PostAsync($"/api/orders/{pending.Id}/checkout", content: null, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString().ShouldBe("orderNotPayable");
    }

    [Fact]
    public async Task Checkout_after_the_expiry_time_expires_the_order_without_waiting_for_the_job()
    {
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, fixture.MovieIds[3], OfferType.Rent);
        var order = await CreateOrderAsync(client);

        fixture.Time.Advance(TimeSpan.FromMinutes(31));
        using var response = await client.PostAsync($"/api/orders/{order.Id}/checkout", content: null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Expired);
    }

    [Fact]
    public async Task A_late_success_after_expiry_marks_the_order_paid()
    {
        // Spec 03 §2: the money was taken, so the order is paid and the entitlements are granted.
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, fixture.MovieIds[0], OfferType.Rent);
        var order = await CreateOrderAsync(client);
        var sessionId = SessionId((await CheckoutAsync(client, order.Id)).CheckoutUrl);

        fixture.Time.Advance(TimeSpan.FromMinutes(31));
        await ExpireDueAsync();
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Expired);

        using var success = await fixture.PostWebhookAsync(FakeWebhookPayload.Succeeded, sessionId, order.Id, order.TotalMinor);

        (await ReadAckAsync(success)).Result.ShouldBe(WebhookProcessingResult.Applied);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(OrderStatus.Paid);
        var library = (await client.GetFromJsonAsync<List<LibraryItem>>("/api/library", Json, Ct))!;
        library.ShouldHaveSingleItem().Type.ShouldBe(EntitlementType.Rental);
    }

    [Fact]
    public async Task Rentals_leave_the_library_when_they_end()
    {
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, fixture.MovieIds[1], OfferType.Rent);
        var order = await CreateOrderAsync(client);
        var sessionId = SessionId((await CheckoutAsync(client, order.Id)).CheckoutUrl);
        using (var success = await fixture.PostWebhookAsync(FakeWebhookPayload.Succeeded, sessionId, order.Id, order.TotalMinor))
        {
            (await ReadAckAsync(success)).Result.ShouldBe(WebhookProcessingResult.Applied);
        }

        (await client.GetFromJsonAsync<List<LibraryItem>>("/api/library", Json, Ct))!.ShouldHaveSingleItem();

        fixture.Time.Advance(TimeSpan.FromHours(48));

        (await client.GetFromJsonAsync<List<LibraryItem>>("/api/library", Json, Ct))!.ShouldBeEmpty();
        await AddToCartAsync(client, fixture.MovieIds[1], OfferType.Rent);
    }

    private async Task ExpireDueAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OrderExpiryJob>().ExpireDueAsync(CancellationToken.None);
    }
}
