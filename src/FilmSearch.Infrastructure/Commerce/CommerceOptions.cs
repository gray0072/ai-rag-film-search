using FilmSearch.Core.Commerce;

namespace FilmSearch.Infrastructure.Commerce;

/// <summary>Orders and checkout (section <c>Commerce</c>).</summary>
public sealed class CommerceOptions
{
    public const string SectionName = "Commerce";

    /// <summary>An unpaid order expires after this time (FR-COM-7).</summary>
    public int PaymentTimeoutMinutes { get; set; } = 30;

    /// <summary>How often the expiry job looks for unpaid orders past their expiry time. 0 turns the job off (tests that drive it themselves).</summary>
    public int ExpiryScanSeconds { get; set; } = 60;

    /// <summary>Where the provider sends the browser after a payment. <c>{orderId}</c> is replaced. Relative to the web app (resolved against <see cref="WebBaseUrl"/> when set; the Vite dev server proxies the fake page).</summary>
    public string SuccessUrl { get; set; } = "/checkout/success?orderId={orderId}";

    /// <summary>Where the provider sends the browser when the user cancels.</summary>
    public string CancelUrl { get; set; } = "/checkout/cancel?orderId={orderId}";

    /// <summary>
    /// Absolute URL of the web app, e.g. <c>http://localhost:5173</c>. Relative <see cref="SuccessUrl"/> and
    /// <see cref="CancelUrl"/> are resolved against it: Stripe sends the browser back to absolute URLs only.
    /// The AppHost sets it to the Vite endpoint. Empty: the URLs stay as they are (fine for the fake provider).
    /// </summary>
    public string? WebBaseUrl { get; set; }

    public TimeSpan PaymentTimeout => TimeSpan.FromMinutes(PaymentTimeoutMinutes);

    /// <summary><paramref name="template"/> with <c>{orderId}</c> replaced, absolute when <see cref="WebBaseUrl"/> is set.</summary>
    public string ReturnUrl(string template, Guid orderId)
    {
        var url = template.Replace("{orderId}", orderId.ToString(), StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(WebBaseUrl) || Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            return url;
        }

        return new Uri(new Uri(WebBaseUrl), url).ToString();
    }
}

/// <summary>Payment provider choice (section <c>Payments</c>, spec 07 §1).</summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary><c>Fake</c> (I09; Development and tests) or <c>Stripe</c> (I15; test mode only).</summary>
    public PaymentProvider Provider { get; set; } = PaymentProvider.Fake;
}
