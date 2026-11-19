using FilmSearch.AI.Assistant.Tools;
using FilmSearch.AI.Prompts;
using FilmSearch.Core.Assistant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FilmSearch.AI.Assistant;

public static class AssistantServiceCollectionExtensions
{
    /// <summary>
    /// The assistant (I11, I12, spec 06): <see cref="IAssistantChat"/> with the own agent loop, the tools (search, details,
    /// similar movies, cart, checkout request) and the rolling summary. Needs <c>AddAiProviders</c> (chat client, prompts),
    /// an <see cref="IConversationStore"/>, <see cref="ICheckoutConfirmations"/> and the Commerce services.
    /// </summary>
    public static IServiceCollection AddAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AssistantOptions>()
            .Bind(configuration.GetSection(AssistantOptions.SectionName))
            .Validate(o => o.MaxToolIterations is >= 1 and <= 20, "Assistant:MaxToolIterations must be between 1 and 20.")
            .Validate(o => o.HistoryMaxMessages is >= 2 and <= 200, "Assistant:HistoryMaxMessages must be between 2 and 200.")
            .Validate(o => o.HistoryTokenBudget is >= 200 and <= 200_000, "Assistant:HistoryTokenBudget must be between 200 and 200000.")
            .Validate(o => o.MaxConversationTokens >= 1000, "Assistant:MaxConversationTokens must be at least 1000.")
            .Validate(o => o.MaxOwnerTokensPerDay >= o.MaxConversationTokens, "Assistant:MaxOwnerTokensPerDay must be at least Assistant:MaxConversationTokens.")
            .Validate(o => o.SearchResultLimit is >= 1 and <= SearchMoviesTool.MaxLimit, "Assistant:SearchResultLimit must be between 1 and 10.")
            .Validate(o => o.SummaryMaxOutputTokens is >= 50 and <= 4000, "Assistant:SummaryMaxOutputTokens must be between 50 and 4000.")
            .Validate(o => o.ConfirmationTtlMinutes is >= 1 and <= 120, "Assistant:ConfirmationTtlMinutes must be between 1 and 120.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.TryAddSingleton<PromptLibrary>();
        services.AddSingleton<ConversationTurnLocks>();
        services.AddSingleton<ConversationSummarizer>();
        services.AddSingleton<AssistantMetrics>();

        // Tools in the order they are declared to the model. Scoped: they use the request's owner and DbContext.
        services.AddScoped<AssistantTool, SearchMoviesTool>();
        services.AddScoped<AssistantTool, GetMovieDetailsTool>();
        services.AddScoped<AssistantTool, FindSimilarMoviesTool>();
        services.AddScoped<AssistantTool, ViewCartTool>();
        services.AddScoped<AssistantTool, AddToCartTool>();
        services.AddScoped<AssistantTool, RemoveFromCartTool>();
        services.AddScoped<AssistantTool, RequestCheckoutTool>();
        services.AddScoped<IAssistantChat, AssistantChat>();

        return services;
    }
}
