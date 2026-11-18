using FilmSearch.Core.Commerce;
using Microsoft.Extensions.Options;

namespace FilmSearch.Infrastructure.Commerce;

/// <summary>The registered gateways; <see cref="Active"/> is the one chosen by <c>Payments:Provider</c> (spec 07 §1).</summary>
public sealed class PaymentGateways(IEnumerable<IPaymentGateway> gateways, IOptions<PaymentOptions> options)
{
    private readonly IReadOnlyList<IPaymentGateway> _gateways = [.. gateways];

    public IPaymentGateway Active => Get(options.Value.Provider)
        ?? throw new InvalidOperationException($"No payment gateway is registered for provider '{options.Value.Provider}'.");

    /// <summary>The gateway of a provider, or null when it is not registered (webhooks of an old provider after a switch).</summary>
    public IPaymentGateway? Get(PaymentProvider provider) => _gateways.FirstOrDefault(g => g.Provider == provider);
}
