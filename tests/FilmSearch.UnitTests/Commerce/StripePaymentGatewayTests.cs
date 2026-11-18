using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FilmSearch.Core.Commerce;
using FilmSearch.Infrastructure.Commerce;
using FilmSearch.Infrastructure.Commerce.StripeGateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StripeException = Stripe.StripeException;

namespace FilmSearch.UnitTests.Commerce;

/// <summary>
/// The Stripe gateway (I15) without the network: webhook parsing with real test mode events (captured with the Stripe
/// CLI, <c>StripeEvents/*.json</c>) signed here like Stripe signs them, and session calls against a stub HTTP handler.
/// </summary>
public sealed class StripePaymentGatewayTests
{
    private const string WebhookSecret = "whsec_unit_test_secret";
    private static readonly Guid PaidOrderId = Guid.Parse("01a1ef74-2a00-74a8-b2a1-e5ca706c04ef");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 18, 16, 20, 0, TimeSpan.Zero));

    [Fact]
    public void A_paid_completed_session_is_a_success_of_the_order_in_its_metadata()
    {
        var body = Fixture("checkout.session.completed.json");

        var parsed = NewGateway().ParseWebhook(Signed(body));

        parsed.ProviderEventId.ShouldBe("evt_1UdBZV20BbvaBZVkZ7DbueWo");
        parsed.ProviderEventType.ShouldBe("checkout.session.completed");
        var e = parsed.Event.ShouldNotBeNull();
        e.Type.ShouldBe(PaymentEventType.Succeeded);
        e.SessionId.ShouldStartWith("cs_test_");
        e.OrderId.ShouldBe(PaidOrderId);
        e.AmountMinor.ShouldBe(1499);
        e.Currency.ShouldBe("USD");
        e.PayloadJson.ShouldBe(body);
    }

    [Fact]
    public void An_expired_session_is_an_expiry()
    {
        var parsed = NewGateway().ParseWebhook(Signed(Fixture("checkout.session.expired.json")));

        var e = parsed.Event.ShouldNotBeNull();
        e.Type.ShouldBe(PaymentEventType.Expired);
        e.OrderId.ShouldBe(Guid.Parse("01a1ef77-557c-7543-8756-c63f87a1d228"));
    }

    [Theory]
    [InlineData("checkout.session.async_payment_succeeded", PaymentEventType.Succeeded)]
    [InlineData("checkout.session.async_payment_failed", PaymentEventType.Failed)]
    public void Asynchronous_payment_results_are_mapped(string type, PaymentEventType expected)
    {
        var body = Fixture("checkout.session.completed.json")
            .Replace("\"type\": \"checkout.session.completed\"", $"\"type\": \"{type}\"", StringComparison.Ordinal);

        NewGateway().ParseWebhook(Signed(body)).Event.ShouldNotBeNull().Type.ShouldBe(expected);
    }

    [Fact]
    public void Events_the_shop_does_not_act_on_are_ignored_not_rejected()
    {
        var gateway = NewGateway();

        // A declined card: Stripe's page shows the error and the customer can try again; the order stays pending.
        var declined = gateway.ParseWebhook(Signed(Fixture("payment_intent.payment_failed.json")));
        declined.Event.ShouldBeNull();
        declined.ProviderEventType.ShouldBe("payment_intent.payment_failed");

        // `stripe trigger checkout.session.completed`: a session without our order id.
        var foreign = gateway.ParseWebhook(Signed(Fixture("checkout.session.completed.no-order.json")));
        foreign.Event.ShouldBeNull();
        foreign.IgnoredReason.ShouldNotBeNull().ShouldContain("order id");

        // A completed session whose asynchronous payment is still processing.
        var unpaid = gateway.ParseWebhook(Signed(Fixture("checkout.session.completed.json")
            .Replace("\"payment_status\": \"paid\"", "\"payment_status\": \"unpaid\"", StringComparison.Ordinal)));
        unpaid.Event.ShouldBeNull();
        unpaid.IgnoredReason.ShouldNotBeNull().ShouldContain("unpaid");
    }

    [Fact]
    public void An_event_of_another_api_version_is_accepted()
    {
        var body = Fixture("checkout.session.completed.json")
            .Replace("\"api_version\": \"2026-10-28.endive\"", "\"api_version\": \"2024-06-20\"", StringComparison.Ordinal);

        NewGateway().ParseWebhook(Signed(body)).Event.ShouldNotBeNull().Type.ShouldBe(PaymentEventType.Succeeded);
    }

    [Fact]
    public void Bad_signatures_are_rejected()
    {
        var gateway = NewGateway();
        var body = Fixture("checkout.session.completed.json");
        var now = _time.GetUtcNow();

        Should.Throw<WebhookSignatureException>(() => gateway.ParseWebhook(new PaymentWebhookRequest(body, new Dictionary<string, string>())));
        Should.Throw<WebhookSignatureException>(() => gateway.ParseWebhook(Signed(body, secret: "whsec_other")));
        Should.Throw<WebhookSignatureException>(() => gateway.ParseWebhook(new PaymentWebhookRequest(
            body.Replace("1499", "1", StringComparison.Ordinal),
            Signed(body).Headers)));
        Should.Throw<WebhookSignatureException>(() => gateway.ParseWebhook(Signed(body, at: now.AddMinutes(-6))));
        Should.Throw<WebhookSignatureException>(() => gateway.ParseWebhook(new PaymentWebhookRequest(body, new Dictionary<string, string> { ["Stripe-Signature"] = "garbage" })));
        Should.Throw<WebhookSignatureException>(() => NewGateway(webhookSecret: null).ParseWebhook(Signed(body)));

        Should.NotThrow(() => gateway.ParseWebhook(Signed(body, at: now.AddMinutes(-4))));
    }

    [Fact]
    public void A_signed_body_that_is_not_an_event_is_a_format_error()
    {
        var gateway = NewGateway();

        Should.Throw<FormatException>(() => gateway.ParseWebhook(Signed("not json")));
        Should.Throw<FormatException>(() => gateway.ParseWebhook(Signed("{}")));
    }

    [Fact]
    public async Task A_session_is_created_from_the_order_with_its_idempotency_key()
    {
        var api = new StubStripeApi((request, body) => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/v1/checkout/sessions"
            ? StubStripeApi.Json(HttpStatusCode.OK, """{"id":"cs_test_1","object":"checkout.session","url":"https://checkout.stripe.com/c/pay/cs_test_1","expires_at":1795023000,"status":"open"}""")
            : StubStripeApi.Json(HttpStatusCode.NotFound, "{}"));
        var orderId = Guid.CreateVersion7();
        var orderExpiresAt = _time.GetUtcNow().AddMinutes(30);

        var session = await NewGateway(api).CreateCheckoutSessionAsync(
            new CreateCheckoutRequest(
                orderId,
                [new CheckoutLineItem("Heat", 1499, "USD"), new CheckoutLineItem("Alien", 399, "USD")],
                1898,
                "USD",
                $"https://shop.example/checkout/success?orderId={orderId}",
                $"https://shop.example/checkout/cancel?orderId={orderId}",
                $"order:{orderId}:attempt:2",
                orderExpiresAt),
            TestContext.Current.CancellationToken);

        session.SessionId.ShouldBe("cs_test_1");
        session.CheckoutUrl.ShouldBe("https://checkout.stripe.com/c/pay/cs_test_1");
        session.ExpiresAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1795023000));

        var (request, form) = api.Requests.ShouldHaveSingleItem();
        request.Headers.GetValues("Idempotency-Key").ShouldHaveSingleItem().ShouldBe($"order:{orderId}:attempt:2");
        request.Headers.Authorization!.Parameter.ShouldBe("sk_test_unit");
        form["mode"].ShouldBe("payment");
        form["client_reference_id"].ShouldBe(orderId.ToString());
        form["metadata[orderId]"].ShouldBe(orderId.ToString());
        form["payment_intent_data[metadata][orderId]"].ShouldBe(orderId.ToString());
        form["line_items[0][price_data][product_data][name]"].ShouldBe("Heat");
        form["line_items[0][price_data][unit_amount]"].ShouldBe("1499");
        form["line_items[0][price_data][currency]"].ShouldBe("usd");
        form["line_items[1][price_data][unit_amount]"].ShouldBe("399");
        form["line_items[1][quantity]"].ShouldBe("1");
        form["success_url"].ShouldBe($"https://shop.example/checkout/success?orderId={orderId}");
        form["cancel_url"].ShouldBe($"https://shop.example/checkout/cancel?orderId={orderId}");
        form["expires_at"].ShouldBe(orderExpiresAt.AddMinutes(30).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Relative_return_urls_are_refused_before_calling_stripe()
    {
        var api = new StubStripeApi((_, _) => StubStripeApi.Json(HttpStatusCode.OK, "{}"));
        var orderId = Guid.CreateVersion7();

        await Should.ThrowAsync<InvalidOperationException>(() => NewGateway(api).CreateCheckoutSessionAsync(
            new CreateCheckoutRequest(orderId, [new CheckoutLineItem("Heat", 1499, "USD")], 1499, "USD", "/checkout/success", "/checkout/cancel", "key", _time.GetUtcNow().AddMinutes(30)),
            TestContext.Current.CancellationToken));
        api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void The_session_outlives_the_order_by_stripes_minimum_lifetime_and_never_beyond_a_day()
    {
        var now = _time.GetUtcNow();

        StripePaymentGateway.SessionExpiresAt(now.AddMinutes(30), now).ShouldBe(now.AddMinutes(60));
        StripePaymentGateway.SessionExpiresAt(now.AddSeconds(5), now).ShouldBe(now.AddSeconds(5).AddMinutes(30));
        StripePaymentGateway.SessionExpiresAt(now.AddHours(30), now).ShouldBeLessThan(now.AddHours(24));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, null, false)]
    [InlineData(HttpStatusCode.NotFound, null, false)]
    [InlineData(HttpStatusCode.BadRequest, "complete", false)]
    [InlineData(HttpStatusCode.BadRequest, "expired", false)]
    [InlineData(HttpStatusCode.BadRequest, "open", true)]
    public async Task Expiring_a_session_that_is_not_open_is_a_no_op(HttpStatusCode expireStatus, string? currentStatus, bool throws)
    {
        var api = new StubStripeApi((request, _) => request.Method == HttpMethod.Post
            ? StubStripeApi.Json(expireStatus, expireStatus == HttpStatusCode.OK
                ? """{"id":"cs_test_1","object":"checkout.session","status":"expired"}"""
                : """{"error":{"type":"invalid_request_error","message":"Not expirable."}}""")
            : StubStripeApi.Json(HttpStatusCode.OK, $$"""{"id":"cs_test_1","object":"checkout.session","status":"{{currentStatus}}"}"""));

        var expire = () => NewGateway(api).ExpireCheckoutSessionAsync("cs_test_1", TestContext.Current.CancellationToken);

        if (throws)
        {
            await Should.ThrowAsync<StripeException>(expire);
        }
        else
        {
            await expire();
        }

        api.Requests[0].Request.RequestUri!.AbsolutePath.ShouldBe("/v1/checkout/sessions/cs_test_1/expire");
    }

    [Theory]
    [InlineData("sk_live_123", "Fake", null, false)]
    [InlineData("rk_test_123", "Fake", null, false)]
    [InlineData("sk_test_123", "Fake", null, true)]
    [InlineData(null, "Fake", null, true)]
    [InlineData("sk_test_123", "Stripe", "whsec_1", true)]
    [InlineData("sk_test_123", "Stripe", null, false)]
    [InlineData(null, "Stripe", "whsec_1", false)]
    public void Startup_accepts_only_test_keys_and_a_complete_stripe_setup(string? secretKey, string provider, string? webhookSecret, bool valid)
    {
        using var services = Services(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = secretKey,
            ["Stripe:WebhookSecret"] = webhookSecret,
            ["Payments:Provider"] = provider,
            ["Commerce:WebBaseUrl"] = "http://localhost:5173",
        });

        var resolve = () =>
        {
            _ = services.GetRequiredService<IOptions<StripeOptions>>().Value;
            _ = services.GetRequiredService<IOptions<PaymentOptions>>().Value;
        };

        if (valid)
        {
            resolve();
        }
        else
        {
            Should.Throw<OptionsValidationException>(resolve);
        }
    }

    [Fact]
    public void Stripe_needs_absolute_return_urls()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_123",
            ["Stripe:WebhookSecret"] = "whsec_1",
            ["Payments:Provider"] = "Stripe",
        };
        using var withoutBase = Services(settings);
        Should.Throw<OptionsValidationException>(() => withoutBase.GetRequiredService<IOptions<PaymentOptions>>().Value);

        settings["Commerce:SuccessUrl"] = "https://shop.example/ok?orderId={orderId}";
        using var absolute = Services(settings);
        absolute.GetRequiredService<IOptions<PaymentOptions>>().Value.Provider.ShouldBe(PaymentProvider.Stripe);
    }

    [Fact]
    public void Return_urls_are_resolved_against_the_web_app()
    {
        var orderId = Guid.CreateVersion7();
        var options = new CommerceOptions { WebBaseUrl = "http://localhost:5173" };

        options.ReturnUrl("/checkout/success?orderId={orderId}", orderId).ShouldBe($"http://localhost:5173/checkout/success?orderId={orderId}");
        options.ReturnUrl("https://shop.example/done", orderId).ShouldBe("https://shop.example/done");
        new CommerceOptions().ReturnUrl("/checkout/cancel?orderId={orderId}", orderId).ShouldBe($"/checkout/cancel?orderId={orderId}");
    }

    [Fact]
    public void The_gateway_is_registered_only_with_a_secret_key()
    {
        using var without = Services(new Dictionary<string, string?>());
        without.GetServices<IPaymentGateway>().ShouldNotContain(g => g.Provider == PaymentProvider.Stripe);

        using var with = Services(new Dictionary<string, string?> { ["Stripe:SecretKey"] = "sk_test_123" });
        with.GetServices<IPaymentGateway>().ShouldContain(g => g.Provider == PaymentProvider.Stripe);
    }

    private static ServiceProvider Services(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCommerceInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private StripePaymentGateway NewGateway(StubStripeApi? api = null, string? webhookSecret = WebhookSecret) => new(
        new StubHttpClientFactory(api ?? new StubStripeApi((_, _) => throw new InvalidOperationException("No Stripe call expected."))),
        Options.Create(new StripeOptions { SecretKey = "sk_test_unit", WebhookSecret = webhookSecret }),
        _time,
        NullLogger<StripePaymentGateway>.Instance);

    /// <summary>A delivery signed as Stripe does: <c>t=…,v1=hex(HMAC-SHA256(secret, "t.body"))</c>.</summary>
    private PaymentWebhookRequest Signed(string body, string secret = WebhookSecret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? _time.GetUtcNow()).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
        return new PaymentWebhookRequest(body, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Stripe-Signature"] = $"t={timestamp},v1={signature}" });
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Commerce", "StripeEvents", name));

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}

/// <summary>Answers Stripe API calls with <c>respond</c> and records each request with its form body.</summary>
internal sealed class StubStripeApi(Func<HttpRequestMessage, IReadOnlyDictionary<string, string>, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, IReadOnlyDictionary<string, string> Form)> Requests { get; } = [];

    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0].Replace('+', ' ')), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : string.Empty);
        Requests.Add((request, form));
        return respond(request, form);
    }
}
