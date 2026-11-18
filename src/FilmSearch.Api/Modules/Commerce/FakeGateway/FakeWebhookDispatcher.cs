using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace FilmSearch.Api.Modules.Commerce.FakeGateway;

/// <summary>
/// Delivers fake webhooks to this API over HTTP, signed and late (spec 07 §3), so the asynchronous confirmation of real
/// providers is visible from day one: the order is still pending when the browser lands on the success page.
/// Every delivery waits for its own due time (on <see cref="TimeProvider"/>), so deliveries can overtake each other.
/// Failed deliveries (5xx, network) are retried by the standard resilience handler of the HTTP client, like a provider would.
/// </summary>
public sealed partial class FakeWebhookDispatcher(
    IHttpClientFactory httpClients,
    FakeWebhookSigner signer,
    IOptions<FakePaymentOptions> options,
    TimeProvider time,
    IServer server,
    ILogger<FakeWebhookDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "FakePaymentWebhooks";
    public const string WebhookPath = "/api/payments/webhooks/fake";

    private readonly Channel<Delivery> _queue = Channel.CreateUnbounded<Delivery>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Queues an event: one delivery plus <see cref="FakePaymentOptions.DuplicateDeliveries"/> copies, each with its own delay.</summary>
    public void Send(FakeWebhookPayload payload, bool isFailure)
    {
        var settings = options.Value;
        var body = JsonSerializer.Serialize(payload, FakeWebhookPayload.Json);
        var now = time.GetUtcNow();

        for (var copy = 0; copy <= settings.DuplicateDeliveries; copy++)
        {
            var delayMs = Random.Shared.Next(settings.WebhookDelayMinMs, Math.Max(settings.WebhookDelayMinMs, settings.WebhookDelayMaxMs) + 1);
            if (isFailure && settings.DeliverFailuresLate)
            {
                delayMs += settings.LateDeliveryDelayMs;
            }

            _queue.Writer.TryWrite(new Delivery(payload.Id, payload.Type, body, now.AddMilliseconds(delayMs)));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var inFlight = new List<Task>();
        try
        {
            await foreach (var delivery in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                inFlight.RemoveAll(t => t.IsCompleted);
                inFlight.Add(DeliverAsync(delivery, stoppingToken));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: queued deliveries are dropped, as a provider outage would.
        }

        await Task.WhenAll(inFlight);
    }

    private async Task DeliverAsync(Delivery delivery, CancellationToken stoppingToken)
    {
        try
        {
            var wait = delivery.DueAt - time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, time, stoppingToken);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri(), WebhookPath))
            {
                Content = new StringContent(delivery.Body, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation(FakeWebhookSigner.HeaderName, signer.Sign(delivery.Body));

            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, stoppingToken);
            LogDelivered(logger, delivery.EventId, delivery.Type, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            LogDeliveryFailed(logger, ex, delivery.EventId, delivery.Type);
        }
    }

    private Uri BaseUri()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.WebhookBaseUrl))
        {
            return new Uri(options.Value.WebhookBaseUrl);
        }

        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var address = addresses.FirstOrDefault(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("The server has no address; set Payments:Fake:WebhookBaseUrl.");

        // Wildcard bindings (http://+:80, http://[::]:80, http://0.0.0.0:80) are reached through localhost.
        var uri = new UriBuilder(address.Replace("://+", "://localhost", StringComparison.Ordinal).Replace("://*", "://localhost", StringComparison.Ordinal));
        if (uri.Host is "[::]" or "0.0.0.0")
        {
            uri.Host = "localhost";
        }

        return uri.Uri;
    }

    /// <summary>
    /// The primary handler of the webhook client. The client only calls this API itself, which in Development uses the
    /// ASP.NET Core development certificate (<c>CN=localhost</c>); that certificate is accepted even when it is not trusted.
    /// </summary>
    public static HttpMessageHandler CreatePrimaryHandler() => new SocketsHttpHandler
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                errors == SslPolicyErrors.None || certificate?.Subject == "CN=localhost",
        },
    };

    private sealed record Delivery(string EventId, string Type, string Body, DateTimeOffset DueAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fake webhook {EventId} ({Type}) delivered: HTTP {StatusCode}")]
    private static partial void LogDelivered(ILogger logger, string eventId, string type, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fake webhook {EventId} ({Type}) could not be delivered")]
    private static partial void LogDeliveryFailed(ILogger logger, Exception exception, string eventId, string type);
}
