namespace FilmSearch.Infrastructure.Commerce.StripeGateway;

/// <summary>Stripe test mode (section <c>Stripe</c>, spec 07 §4). Keys live in user secrets or environment variables only.</summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>The only accepted key prefix: live keys (<c>sk_live_</c>, restricted <c>rk_…</c>) are refused at startup (NFR-7).</summary>
    public const string TestKeyPrefix = "sk_test_";

    /// <summary>Secret API key, <c>sk_test_…</c>. Empty: Stripe is not registered.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Signing secret of the webhook endpoint, <c>whsec_…</c> (<c>stripe listen</c> prints it for local development).</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>How old a webhook signature may be (replay protection). Stripe's libraries use 5 minutes.</summary>
    public int WebhookToleranceSeconds { get; set; } = 300;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}
