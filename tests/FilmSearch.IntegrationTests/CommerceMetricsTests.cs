using System.Net;
using FilmSearch.Api.Modules.Commerce.FakeGateway;
using FilmSearch.Core.Catalog;
using FilmSearch.IntegrationTests.Infrastructure;
using static FilmSearch.IntegrationTests.Infrastructure.CommerceFixtureBase;

namespace FilmSearch.IntegrationTests;

/// <summary>Commerce metrics (I18, spec 10 §3): <c>payments.webhooks</c> by type and result, <c>orders.status_changes</c>.</summary>
public sealed class CommerceMetricsTests(CommerceApiFixture fixture) : IClassFixture<CommerceApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Webhook_results_and_order_status_changes_are_counted()
    {
        using var recorder = new MeterRecorder(fixture.Factory.Services, "payments.webhooks", "orders.status_changes");
        using var client = fixture.CreateClient();
        await AddToCartAsync(client, fixture.MovieIds[0], OfferType.Buy);
        var order = await CreateOrderAsync(client);
        var checkout = await CheckoutAsync(client, order.Id);
        var sessionId = SessionId(checkout.CheckoutUrl);

        const string eventId = "evt_metrics_paid";
        (await fixture.PostWebhookAsync(FakeWebhookPayload.Succeeded, sessionId, order.Id, order.TotalMinor, eventId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await fixture.PostWebhookAsync(FakeWebhookPayload.Succeeded, sessionId, order.Id, order.TotalMinor, eventId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await fixture.PostRawWebhookAsync("{}", "t=1,v1=bad")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var webhooks = recorder.Of("payments.webhooks");
        webhooks.Select(m => ((string?)m.Tag("type"), (string?)m.Tag("result"))).ShouldBe(
            [("succeeded", "applied"), ("succeeded", "duplicate"), ("unknown", "rejected")]);
        webhooks.ShouldAllBe(m => (string?)m.Tag("provider") == "fake");

        var changes = recorder.Of("orders.status_changes").Select(m => ((string?)m.Tag("from"), (string?)m.Tag("to"))).ToList();
        changes.ShouldBe([("created", "pendingPayment"), ("pendingPayment", "paid")]);
        (await GetOrderAsync(client, order.Id)).Status.ShouldBe(FilmSearch.Core.Commerce.OrderStatus.Paid);
    }
}
