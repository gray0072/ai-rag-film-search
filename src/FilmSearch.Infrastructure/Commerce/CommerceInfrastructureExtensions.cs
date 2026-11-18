using FilmSearch.Core.Commerce;
using FilmSearch.Infrastructure.Commerce.StripeGateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FilmSearch.Infrastructure.Commerce;

public static class CommerceInfrastructureExtensions
{
    /// <summary>
    /// Cart, checkout, library, webhook processing, the order expiry job and the Stripe gateway (when <c>Stripe:SecretKey</c>
    /// is set). The Fake gateway (<see cref="IPaymentGateway"/>) is registered by the API host (I09). The service that runs
    /// the expiry job on a timer is registered by the Worker (<see cref="AddOrderExpiryService"/>).
    /// </summary>
    public static IServiceCollection AddCommerceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<CommerceOptions>()
            .Bind(configuration.GetSection(CommerceOptions.SectionName))
            .Validate(o => o.PaymentTimeoutMinutes is >= 1 and <= 24 * 60, "Commerce:PaymentTimeoutMinutes must be between 1 and 1440.")
            .Validate(o => o.ExpiryScanSeconds is >= 0 and <= 3600, "Commerce:ExpiryScanSeconds must be between 0 and 3600.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.SuccessUrl) && !string.IsNullOrWhiteSpace(o.CancelUrl), "Commerce:SuccessUrl and Commerce:CancelUrl are required.")
            .Validate(o => string.IsNullOrWhiteSpace(o.WebBaseUrl) || Uri.IsWellFormedUriString(o.WebBaseUrl, UriKind.Absolute), "Commerce:WebBaseUrl must be an absolute URL.")
            .ValidateOnStart();
        services.AddOptions<PaymentOptions>()
            .Bind(configuration.GetSection(PaymentOptions.SectionName))
            .Validate(o => Enum.IsDefined(o.Provider), "Payments:Provider must be 'Fake' or 'Stripe'.")
            .ValidateOnStart();

        services.AddStripeGateway(configuration);

        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<IPaymentWebhookProcessor, PaymentWebhookProcessor>();
        services.AddSingleton<PaymentGateways>();

        services.AddScoped<OrderExpiryJob>();

        return services;
    }

    /// <summary>Runs <see cref="OrderExpiryJob"/> every <c>Commerce:ExpiryScanSeconds</c> (the Worker, I16).</summary>
    public static IServiceCollection AddOrderExpiryService(this IServiceCollection services)
    {
        services.AddHostedService<OrderExpiryService>();
        return services;
    }

    /// <summary>
    /// Stripe test mode (spec 07 §4). Registered when a secret key is configured, so webhooks of Stripe sessions are
    /// still accepted after switching <c>Payments:Provider</c> back to Fake. Startup fails on a live key (NFR-7) and when
    /// Stripe is the active provider without a webhook secret or absolute return URLs.
    /// </summary>
    private static void AddStripeGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .Validate(
                o => !o.IsConfigured || o.SecretKey!.StartsWith(StripeOptions.TestKeyPrefix, StringComparison.Ordinal),
                $"Stripe:SecretKey must be a test mode key ({StripeOptions.TestKeyPrefix}…): live keys are refused.")
            .Validate(o => o.WebhookToleranceSeconds is >= 1 and <= 3600, "Stripe:WebhookToleranceSeconds must be between 1 and 3600.")
            .ValidateOnStart();
        services.AddOptions<PaymentOptions>()
            .Validate<IOptions<StripeOptions>>(
                (payments, stripe) => payments.Provider != PaymentProvider.Stripe
                    || (stripe.Value.IsConfigured && !string.IsNullOrWhiteSpace(stripe.Value.WebhookSecret)),
                "Payments:Provider is Stripe: set Stripe:SecretKey (sk_test_…) and Stripe:WebhookSecret (whsec_…, printed by `stripe listen`) in user secrets.")
            .Validate<IOptions<CommerceOptions>>(
                (payments, commerce) => payments.Provider != PaymentProvider.Stripe
                    || Uri.IsWellFormedUriString(commerce.Value.ReturnUrl(commerce.Value.SuccessUrl, Guid.Empty), UriKind.Absolute),
                "Payments:Provider is Stripe: Stripe needs absolute return URLs; set Commerce:WebBaseUrl (the AppHost sets it to the web app).");

        if (string.IsNullOrWhiteSpace(configuration[$"{StripeOptions.SectionName}:{nameof(StripeOptions.SecretKey)}"]))
        {
            return;
        }

        services.AddHttpClient(StripePaymentGateway.HttpClientName);
        services.AddSingleton<StripePaymentGateway>();
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<StripePaymentGateway>());
    }
}
