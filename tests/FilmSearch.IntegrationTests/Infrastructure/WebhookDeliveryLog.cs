using System.Collections.Concurrent;
using System.Net.Http.Json;
using FilmSearch.Api.Modules.Commerce.FakeGateway;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>Every webhook delivery of the fake provider with the API's answer, in arrival order.</summary>
public sealed class WebhookDeliveryLog
{
    private readonly ConcurrentQueue<Delivery> _deliveries = new();

    public IReadOnlyList<Delivery> Deliveries => [.. _deliveries];

    public IReadOnlyList<Delivery> ForSession(string sessionId) => [.. _deliveries.Where(d => d.Payload.Data.SessionId == sessionId)];

    /// <summary>Waits until <paramref name="condition"/> holds for the deliveries of a session.</summary>
    public async Task<IReadOnlyList<Delivery>> WaitForAsync(string sessionId, Func<IReadOnlyList<Delivery>, bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (true)
        {
            var deliveries = ForSession(sessionId);
            if (condition(deliveries))
            {
                return deliveries;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Webhook deliveries of {sessionId}: {string.Join(", ", deliveries.Select(d => $"{d.Payload.Type}={d.StatusCode}"))}");
            }

            await Task.Delay(50);
        }
    }

    public sealed record Delivery(FakeWebhookPayload Payload, int StatusCode, string ResponseBody);

    /// <summary>Sends to the TestServer and records the request and the answer.</summary>
    public sealed class Handler(WebhookDeliveryLog log, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = await request.Content!.ReadFromJsonAsync<FakeWebhookPayload>(FakeWebhookPayload.Json, cancellationToken);
            var response = await base.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            log._deliveries.Enqueue(new Delivery(payload!, (int)response.StatusCode, body));
            return response;
        }
    }
}
