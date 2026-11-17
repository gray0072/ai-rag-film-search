using FilmSearch.AI.Diagnostics;
using FilmSearch.AI.Embeddings;
using FilmSearch.AI.Health;
using FilmSearch.AI.Telemetry;
using FilmSearch.Core.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FilmSearch.AI.Providers;

// RemoveAllResilienceHandlers is marked experimental; it is the supported way to opt a client out of
// ConfigureHttpClientDefaults (ServiceDefaults adds the standard resilience handler to every client).
#pragma warning disable EXTEXP0001

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the AI providers from the <c>AI</c> section (spec 06 §1):
    /// <list type="bullet">
    /// <item><c>IChatClient</c> and <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c> of the active profiles (unkeyed);</item>
    /// <item>the same for any named profile as keyed services: <c>[FromKeyedServices("openrouter")] IChatClient</c>;</item>
    /// <item><see cref="ITextEmbedder"/> over the embedding profiles;</item>
    /// <item>health checks of the active providers (tag <c>ai</c>, Degraded on failure).</item>
    /// </list>
    /// Options are validated on start. Clients are singletons and are created on first use.
    /// </summary>
    public static IHostApplicationBuilder AddAiProviders(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var section = builder.Configuration.GetSection(AiOptions.SectionName);

        services.AddOptions<AiOptions>().Bind(section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();
        services.AddMetrics();
        services.AddSingleton<AiMetrics>();
        services.AddSingleton<AiClientFactory>();

        // Calls to local models take minutes on a laptop: the default resilience handler (10 s per attempt) would cut
        // them, so it is removed. Timeout and retries come from the profile (OpenAI client pipeline, AiClientFactory).
        var http = services.AddHttpClient(AiClientFactory.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        http.RemoveAllResilienceHandlers();
        if (builder.Environment.IsDevelopment() && section.GetValue<bool>(nameof(AiOptions.LogRawHttp)))
        {
            services.AddTransient<RawHttpLoggingHandler>();
            http.AddHttpMessageHandler<RawHttpLoggingHandler>();
        }

        // Keyed by profile name: any profile can be resolved without code changes. AnyKey caches one instance per key.
        services.AddKeyedSingleton<IChatClient>(KeyedService.AnyKey, (sp, key) =>
            sp.GetRequiredService<AiClientFactory>().CreateChatClient(ProfileName(key)));
        services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(KeyedService.AnyKey, (sp, key) =>
            sp.GetRequiredService<AiClientFactory>().CreateEmbeddingGenerator(ProfileName(key)));

        // Unkeyed = active profile. Business code depends only on these two interfaces (ADR-0002).
        services.AddSingleton(sp => sp.GetRequiredKeyedService<IChatClient>(
            sp.GetRequiredService<IOptions<AiOptions>>().Value.ActiveChatProfile));
        services.AddSingleton(sp => sp.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(
            sp.GetRequiredService<IOptions<AiOptions>>().Value.ActiveEmbeddingProfile));

        // Search code embeds through this contract of FilmSearch.Core (I06), not through the generator directly.
        services.AddSingleton<ITextEmbedder, ProfileTextEmbedder>();

        if (section.GetValue(nameof(AiOptions.HealthChecksEnabled), defaultValue: true))
        {
            services.AddHttpClient(AiProviderHealthCheck.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5))
                .RemoveAllResilienceHandlers();
            services.AddHealthChecks()
                .Add(HealthCheck("ai-chat", AiProfileKind.Chat))
                .Add(HealthCheck("ai-embeddings", AiProfileKind.Embedding));
        }

        return builder;
    }

    private static HealthCheckRegistration HealthCheck(string name, AiProfileKind kind) =>
        new(name,
            sp => ActivatorUtilities.CreateInstance<AiProviderHealthCheck>(sp, kind),
            HealthStatus.Degraded,
            ["ai"]);

    private static string ProfileName(object? key) =>
        key as string ?? throw new InvalidOperationException("AI clients are keyed by profile name (string).");
}
