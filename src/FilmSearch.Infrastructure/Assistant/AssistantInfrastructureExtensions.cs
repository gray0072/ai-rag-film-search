using FilmSearch.Core.Assistant;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FilmSearch.Infrastructure.Assistant;

public static class AssistantInfrastructureExtensions
{
    /// <summary>
    /// Storage of conversations, messages and feedback (I11) and checkout confirmations (I12; needs the Commerce module).
    /// The agent loop is registered by <c>FilmSearch.AI</c>.
    /// </summary>
    public static IServiceCollection AddAssistantInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IConversationStore, ConversationStore>();
        services.AddScoped<ICheckoutConfirmations, CheckoutConfirmations>();
        return services;
    }
}
