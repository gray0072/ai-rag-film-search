using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FilmSearch.Api.Modules.Commerce;
using FilmSearch.Api.Modules.Commerce.FakeGateway;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>
/// The API on its own database with a few test movies, for commerce tests. Its own database because the expiry job
/// and time travel touch every order in a database. Each <see cref="CreateClient"/> is a new browser: a new owner cookie.
/// </summary>
public abstract class CommerceFixtureBase(PostgresFixture postgres, IReadOnlyDictionary<string, string?>? settings = null, TimeProvider? time = null)
    : IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly MovieImport[] TestMovies =
    [
        Movie(995_001, "Commerce Movie One", 1994),
        Movie(995_002, "Commerce Movie Two", 2010),
        Movie(995_003, "Commerce Movie Three", 2024),
        Movie(995_004, "Commerce Movie Four", 1960),
    ];

    public FilmSearchApiFactory Factory { get; private set; } = null!;

    /// <summary>Ids of the test movies, in the order of <see cref="TestMovies"/>.</summary>
    public IReadOnlyList<Guid> MovieIds { get; private set; } = [];

    public async ValueTask InitializeAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync($"commerce_{GetType().Name.ToLowerInvariant()}_{Guid.NewGuid():N}");
        Factory = new FilmSearchApiFactory(postgres, connectionString, settings, time);

        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICatalogWriter>().UpsertMoviesAsync(TestMovies, MovieSource.Manual, CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await db.Set<Movie>().Where(m => m.TmdbId >= 995_001 && m.TmdbId <= 995_004).ToDictionaryAsync(m => m.TmdbId!.Value, m => m.Id);
        MovieIds = [.. TestMovies.Select(m => ids[m.TmdbId])];
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>A new browser: HTTPS (the owner cookie is Secure), redirects not followed.</summary>
    public HttpClient CreateClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    public static async Task<CartView> AddToCartAsync(HttpClient client, Guid movieId, OfferType offerType)
    {
        using var response = await client.PostAsJsonAsync("/api/cart/items", new { movieId, offerType }, Json);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CartView>(Json))!;
    }

    public static async Task<HttpResponseMessage> PostOrderAsync(HttpClient client, string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        if (idempotencyKey is not null)
        {
            request.Headers.Add(CommerceModule.IdempotencyKeyHeader, idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    public static async Task<OrderView> CreateOrderAsync(HttpClient client, string? idempotencyKey = null)
    {
        using var response = await PostOrderAsync(client, idempotencyKey ?? Guid.NewGuid().ToString());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OrderView>(Json))!;
    }

    public static async Task<CheckoutStart> CheckoutAsync(HttpClient client, Guid orderId)
    {
        using var response = await client.PostAsync($"/api/orders/{orderId}/checkout", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CheckoutStart>(Json))!;
    }

    /// <summary>Presses a button of the fake page (<c>pay</c>, <c>decline</c>, <c>cancel</c>).</summary>
    public static Task<HttpResponseMessage> PressAsync(HttpClient client, string checkoutUrl, string button) =>
        client.PostAsync($"{checkoutUrl}/{button}", content: null);

    public static string SessionId(string checkoutUrl) => checkoutUrl[(checkoutUrl.LastIndexOf('/') + 1)..];

    public static async Task<OrderView> GetOrderAsync(HttpClient client, Guid orderId) =>
        (await client.GetFromJsonAsync<OrderView>($"/api/orders/{orderId}", Json))!;

    /// <summary>Polls the order like the success page does (spec 07 §2) until it has <paramref name="status"/>.</summary>
    public static async Task<OrderView> WaitForStatusAsync(HttpClient client, Guid orderId, OrderStatus status, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (true)
        {
            var order = await GetOrderAsync(client, orderId);
            if (order.Status == status)
            {
                return order;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Order {orderId} is {order.Status}, expected {status}.");
            }

            await Task.Delay(50);
        }
    }

    /// <summary>Sends a webhook signed like the fake provider does, straight to the endpoint (any order, any number of times).</summary>
    public async Task<HttpResponseMessage> PostWebhookAsync(string type, string sessionId, Guid orderId, long amountMinor, string? eventId = null, string currency = "USD")
    {
        var payload = new FakeWebhookPayload(
            eventId ?? $"evt_test_{Guid.NewGuid():N}",
            type,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            new FakeWebhookData(sessionId, orderId, amountMinor, currency));
        var body = JsonSerializer.Serialize(payload, FakeWebhookPayload.Json);
        return await PostRawWebhookAsync(body, Factory.Services.GetRequiredService<FakeWebhookSigner>().Sign(body));
    }

    public async Task<HttpResponseMessage> PostRawWebhookAsync(string body, string? signature)
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, FakeWebhookDispatcher.WebhookPath)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation(FakeWebhookSigner.HeaderName, signature);
        }

        return await client.SendAsync(request);
    }

    public static async Task<WebhookAck> ReadAckAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<WebhookAck>(Json))!;
    }

    /// <summary>Runs <paramref name="query"/> against the fixture's database.</summary>
    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static MovieImport Movie(int tmdbId, string title, int year) => new()
    {
        TmdbId = tmdbId,
        OriginalTitle = title,
        OriginalLanguage = "en",
        ReleaseDate = new DateOnly(year, 6, 1),
        RuntimeMinutes = 100,
        VoteAverage = 7.0m,
        VoteCount = 100,
        Popularity = 1,
        Translations = new Dictionary<string, TranslationImport>
        {
            ["en"] = new(title, $"Overview of {title}.", null),
            ["ru"] = new($"{title} (ru)", null, null),
        },
    };
}

/// <summary>
/// Stripe as the active provider (I15): the real gateway and SDK against <see cref="FakeStripeApi"/>, webhooks signed
/// with the test secret and sent straight to the endpoint. The expiry job does not run by itself.
/// </summary>
public sealed class StripeCommerceFixture(PostgresFixture postgres) : CommerceFixtureBase(postgres, new Dictionary<string, string?>
{
    ["Payments:Provider"] = "Stripe",
    ["Stripe:SecretKey"] = "sk_test_integration",
    ["Stripe:WebhookSecret"] = WebhookSecret,
    ["Commerce:WebBaseUrl"] = "https://shop.example",
    ["Commerce:ExpiryScanSeconds"] = "0",
})
{
    public const string WebhookSecret = "whsec_integration";

    /// <summary>Sends a Stripe event about a Checkout Session, signed like Stripe signs it.</summary>
    public Task<HttpResponseMessage> PostStripeEventAsync(
        string type,
        string sessionId,
        Guid orderId,
        long amountMinor,
        string paymentStatus = "paid",
        string? eventId = null,
        string currency = "usd")
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = eventId ?? $"evt_test_{Guid.NewGuid():N}",
            ["object"] = "event",
            ["api_version"] = "2026-10-28.endive",
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["livemode"] = false,
            ["pending_webhooks"] = 1,
            ["type"] = type,
            ["data"] = new Dictionary<string, object?>
            {
                ["object"] = new Dictionary<string, object?>
                {
                    ["id"] = sessionId,
                    ["object"] = "checkout.session",
                    ["amount_total"] = amountMinor,
                    ["currency"] = currency,
                    ["client_reference_id"] = orderId.ToString(),
                    ["metadata"] = new Dictionary<string, string> { ["orderId"] = orderId.ToString() },
                    ["mode"] = "payment",
                    ["payment_status"] = paymentStatus,
                    ["status"] = type == "checkout.session.expired" ? "expired" : "complete",
                },
            },
        });
        return PostSignedStripeAsync(body, WebhookSecret, DateTimeOffset.UtcNow);
    }

    public async Task<HttpResponseMessage> PostSignedStripeAsync(string body, string secret, DateTimeOffset signedAt)
    {
        var timestamp = signedAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret),
            System.Text.Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Post, PaymentWebhookEndpoints.StripePath)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", $"t={timestamp},v1={signature}");
        return await client.SendAsync(request);
    }
}

/// <summary>Real clock, webhooks without delay.</summary>
public sealed class CommerceApiFixture(PostgresFixture postgres) : CommerceFixtureBase(postgres);

/// <summary>The fake provider's switches on: every event delivered three times, failures delivered late (out of order).</summary>
public sealed class FakeGatewaySwitchesFixture(PostgresFixture postgres) : CommerceFixtureBase(postgres, new Dictionary<string, string?>
{
    ["Payments:Fake:DuplicateDeliveries"] = "2",
    ["Payments:Fake:DeliverFailuresLate"] = "true",
    ["Payments:Fake:LateDeliveryDelayMs"] = "1500",
});

/// <summary>
/// A <see cref="FakeTimeProvider"/> for the whole app: webhooks are due 2 s after the button on the fake clock, and the
/// expiry job does not run by itself (tests call <c>OrderExpiryJob</c> after moving the clock).
/// </summary>
public sealed class CommerceTimeFixture : CommerceFixtureBase
{
    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["Commerce:ExpiryScanSeconds"] = "0",
        ["Payments:Fake:WebhookDelayMinMs"] = "2000",
        ["Payments:Fake:WebhookDelayMaxMs"] = "2000",
    };

    public CommerceTimeFixture(PostgresFixture postgres)
        : this(postgres, new FakeTimeProvider(DateTimeOffset.UtcNow))
    {
    }

    private CommerceTimeFixture(PostgresFixture postgres, FakeTimeProvider time)
        : base(postgres, Settings, time)
    {
        Time = time;
    }

    public FakeTimeProvider Time { get; }
}
