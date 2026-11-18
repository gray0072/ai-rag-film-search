using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>
/// The few Stripe API calls the gateway makes (create, expire and get a Checkout Session), answered in memory, so
/// integration tests run the real <c>StripePaymentGateway</c> and SDK without the network. Like Stripe, a repeated
/// idempotency key returns the first session, and only an open session can be expired.
/// </summary>
public sealed class FakeStripeApi : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _byIdempotencyKey = new(StringComparer.Ordinal);

    public ConcurrentQueue<string> Calls { get; } = new();

    public Session? Get(string sessionId) => _sessions.GetValueOrDefault(sessionId);

    /// <summary>The customer paid on the hosted page (the test then sends the webhook).</summary>
    public void Complete(string sessionId) => _sessions[sessionId] = _sessions[sessionId] with { Status = "complete" };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Calls.Enqueue($"{request.Method} {path}");
        var form = request.Content is null ? new Dictionary<string, string>() : ParseForm(await request.Content.ReadAsStringAsync(cancellationToken));

        if (request.Method == HttpMethod.Post && path == "/v1/checkout/sessions")
        {
            var key = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : Guid.NewGuid().ToString();
            var id = _byIdempotencyKey.GetOrAdd(key, _ =>
            {
                var session = new Session(
                    $"cs_test_{Guid.NewGuid():N}",
                    form["metadata[orderId]"],
                    Enumerable.Range(0, 100).TakeWhile(i => form.ContainsKey($"line_items[{i}][price_data][unit_amount]"))
                        .Sum(i => long.Parse(form[$"line_items[{i}][price_data][unit_amount]"], System.Globalization.CultureInfo.InvariantCulture)),
                    form["line_items[0][price_data][currency]"],
                    long.Parse(form["expires_at"], System.Globalization.CultureInfo.InvariantCulture),
                    form["success_url"],
                    "open");
                _sessions[session.Id] = session;
                return session.Id;
            });
            return Json(HttpStatusCode.OK, _sessions[id]);
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts is ["v1", "checkout", "sessions", var sessionId, ..] && _sessions.TryGetValue(sessionId, out var existing))
        {
            if (request.Method == HttpMethod.Get && parts.Length == 4)
            {
                return Json(HttpStatusCode.OK, existing);
            }

            if (request.Method == HttpMethod.Post && parts is [.., "expire"])
            {
                if (existing.Status != "open")
                {
                    return Error(HttpStatusCode.BadRequest, "Only Checkout Sessions with a status of open can be expired.");
                }

                var expired = existing with { Status = "expired" };
                _sessions[sessionId] = expired;
                return Json(HttpStatusCode.OK, expired);
            }
        }

        return Error(HttpStatusCode.NotFound, $"No such resource: {path}");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, Session session) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = session.Id,
                ["object"] = "checkout.session",
                ["url"] = $"https://checkout.stripe.com/c/pay/{session.Id}",
                ["status"] = session.Status,
                ["amount_total"] = session.AmountTotal,
                ["currency"] = session.Currency,
                ["expires_at"] = session.ExpiresAt,
                ["success_url"] = session.SuccessUrl,
                ["metadata"] = new Dictionary<string, string> { ["orderId"] = session.OrderId },
            }),
            Encoding.UTF8,
            "application/json"),
    };

    private static HttpResponseMessage Error(HttpStatusCode status, string message) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { error = new { type = "invalid_request_error", message } }),
            Encoding.UTF8,
            "application/json"),
    };

    private static Dictionary<string, string> ParseForm(string body) => body
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split('=', 2))
        .ToDictionary(p => Uri.UnescapeDataString(p[0].Replace('+', ' ')), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : string.Empty);

    public sealed record Session(string Id, string OrderId, long AmountTotal, string Currency, long ExpiresAt, string SuccessUrl, string Status);
}
