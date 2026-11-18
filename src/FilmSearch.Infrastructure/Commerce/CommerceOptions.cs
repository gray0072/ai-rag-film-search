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

    /// <summary>Where the provider sends the browser after a payment. <c>{orderId}</c> is replaced. Relative to the web app (the Vite dev server proxies the fake page).</summary>
    public string SuccessUrl { get; set; } = "/checkout/success?orderId={orderId}";

    /// <summary>Where the provider sends the browser when the user cancels.</summary>
    public string CancelUrl { get; set; } = "/checkout/cancel?orderId={orderId}";

    public TimeSpan PaymentTimeout => TimeSpan.FromMinutes(PaymentTimeoutMinutes);
}

/// <summary>Payment provider choice (section <c>Payments</c>, spec 07 §1).</summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary><c>Fake</c> (I09) or <c>Stripe</c> (I15).</summary>
    public PaymentProvider Provider { get; set; } = PaymentProvider.Fake;
}
