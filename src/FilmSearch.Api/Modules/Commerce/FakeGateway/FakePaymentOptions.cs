namespace FilmSearch.Api.Modules.Commerce.FakeGateway;

/// <summary>
/// The fake payment provider (section <c>Payments:Fake</c>, spec 07 §3). The switches simulate what real providers do
/// to webhooks: delay, duplicate delivery and out-of-order delivery.
/// </summary>
public sealed class FakePaymentOptions
{
    public const string SectionName = "Payments:Fake";

    /// <summary>HMAC key of the webhook signature. Empty: a random key per process (the fake signs and verifies in the same process).</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Each webhook is delivered after a random delay between the min and the max (spec: 1–3 s).</summary>
    public int WebhookDelayMinMs { get; set; } = 1000;

    public int WebhookDelayMaxMs { get; set; } = 3000;

    /// <summary>Extra deliveries of every event (same event id): 2 delivers each event three times.</summary>
    public int DuplicateDeliveries { get; set; }

    /// <summary>Out-of-order delivery: failure events wait <see cref="LateDeliveryDelayMs"/> longer, so a later success overtakes them.</summary>
    public bool DeliverFailuresLate { get; set; }

    public int LateDeliveryDelayMs { get; set; } = 5000;

    /// <summary>Base URL of this API for webhook calls. Empty: the first address the server listens on (https preferred).</summary>
    public string? WebhookBaseUrl { get; set; }

    /// <summary>Accepted age of a signature timestamp (replay protection).</summary>
    public int SignatureToleranceSeconds { get; set; } = 300;
}
