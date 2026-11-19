using System.Diagnostics.Metrics;
using System.Text.Json;
using FilmSearch.Core.Commerce;

namespace FilmSearch.Infrastructure.Commerce;

/// <summary>
/// Commerce metrics (meter <c>FilmSearch</c>, spec 10 §3): webhook deliveries by provider, event type and result, and
/// order status changes. A rising <c>result=error</c> or <c>rejected</c> count, or orders that stay pending, are the first
/// signs of a broken payment integration.
/// </summary>
public sealed class CommerceMetrics
{
    /// <summary>Webhook result: the signature or body was invalid (400).</summary>
    public const string Rejected = "rejected";

    /// <summary>Webhook result: a verified event of a type the shop does not act on (200, not stored).</summary>
    public const string Unhandled = "unhandled";

    /// <summary>Webhook result: processing threw (500, the provider retries).</summary>
    public const string Error = "error";

    /// <summary>The "from" tag of a new order.</summary>
    public const string Created = "created";

    private const string MeterName = "FilmSearch";

    private readonly Counter<long> _webhooks;
    private readonly Counter<long> _statusChanges;

    public CommerceMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _webhooks = meter.CreateCounter<long>(
            "payments.webhooks", unit: "{event}", description: "Payment webhook deliveries by provider, event type and result.");
        _statusChanges = meter.CreateCounter<long>(
            "orders.status_changes", unit: "{change}", description: "Order status changes (from, to); from=created for new orders.");
    }

    /// <summary><paramref name="result"/>: a <see cref="WebhookProcessingResult"/> name, <see cref="Rejected"/>, <see cref="Unhandled"/> or <see cref="Error"/>.</summary>
    public void RecordWebhook(PaymentProvider provider, string type, string result) =>
        _webhooks.Add(
            1,
            new KeyValuePair<string, object?>("provider", Name(provider.ToString())),
            new("type", type),
            new("result", result));

    public void RecordWebhook(PaymentProvider provider, string type, WebhookProcessingResult result) =>
        RecordWebhook(provider, type, Name(result.ToString()));

    /// <summary>A committed change of an order's status; <paramref name="from"/> is null for a new order.</summary>
    public void RecordStatusChange(OrderStatus? from, OrderStatus to) =>
        _statusChanges.Add(
            1,
            new KeyValuePair<string, object?>("from", from is { } status ? Name(status.ToString()) : Created),
            new("to", Name(to.ToString())));

    /// <summary>Tag value of an enum name: camelCase, as in the JSON of the API.</summary>
    public static string Name(string value) => JsonNamingPolicy.CamelCase.ConvertName(value);
}
