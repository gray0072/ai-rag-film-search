using FilmSearch.Core.Commerce;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FilmSearch.Infrastructure.Commerce;

public static class CommerceInfrastructureExtensions
{
    /// <summary>
    /// Cart, checkout, library, webhook processing and order expiry. Payment gateways (<see cref="IPaymentGateway"/>)
    /// are registered by the host: the Fake gateway by the API (I09), Stripe here in I15.
    /// </summary>
    public static IServiceCollection AddCommerceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<CommerceOptions>()
            .Bind(configuration.GetSection(CommerceOptions.SectionName))
            .Validate(o => o.PaymentTimeoutMinutes is >= 1 and <= 24 * 60, "Commerce:PaymentTimeoutMinutes must be between 1 and 1440.")
            .Validate(o => o.ExpiryScanSeconds is >= 0 and <= 3600, "Commerce:ExpiryScanSeconds must be between 0 and 3600.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.SuccessUrl) && !string.IsNullOrWhiteSpace(o.CancelUrl), "Commerce:SuccessUrl and Commerce:CancelUrl are required.")
            .ValidateOnStart();
        services.AddOptions<PaymentOptions>()
            .Bind(configuration.GetSection(PaymentOptions.SectionName))
            .Validate(o => o.Provider == PaymentProvider.Fake, "Payments:Provider: only 'Fake' is implemented; Stripe comes in I15.")
            .ValidateOnStart();

        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<IPaymentWebhookProcessor, PaymentWebhookProcessor>();
        services.AddSingleton<PaymentGateways>();

        services.AddScoped<OrderExpiryJob>();
        services.AddHostedService<OrderExpiryService>();

        return services;
    }
}
