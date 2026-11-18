using FilmSearch.Core.Assistant;
using FilmSearch.Core.Owners;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FilmSearch.Infrastructure.Assistant;

public static class AssistantInfrastructureExtensions
{
    /// <summary>
    /// Storage of conversations, messages and feedback (I11) and checkout confirmations (I12; needs the Commerce module), and their login merge (I17).
    /// The agent loop is registered by <c>FilmSearch.AI</c>.
    /// </summary>
    public static IServiceCollection AddAssistantInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IConversationStore, ConversationStore>();
        services.AddScoped<ICheckoutConfirmations, CheckoutConfirmations>();
        services.AddScoped<IOwnerMergeHandler, AssistantOwnerMergeHandler>();
        return services;
    }
}
