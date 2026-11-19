using System.ClientModel;
using System.ClientModel.Primitives;
using FilmSearch.AI.Health;
using FilmSearch.AI.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace FilmSearch.AI.Providers;

/// <summary>
/// Builds <see cref="IChatClient"/> and <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> from a named profile.
/// This is the only place that knows the provider SDK: the OpenAI .NET client pointed at the profile endpoint, which
/// works for LM Studio, OpenRouter and any other OpenAI-compatible API (ADR-0002).
/// <para>
/// Chat pipeline, outermost first (spec 06 §1): profile defaults (model, temperature, max tokens, reasoning) → OpenTelemetry
/// GenAI span → logging → usage and cost tracking → provider client → HTTP client of the profile (retry, circuit
/// breaker and attempt timeout, <see cref="AiHttpResilience"/>).
/// </para>
/// </summary>
internal sealed class AiClientFactory(
    IOptions<AiOptions> options,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    AiMetrics metrics,
    IServiceProvider services)
{
    /// <summary>Prefix of the named HTTP clients of provider calls: one client per profile (see AddAiProviders).</summary>
    internal const string HttpClientPrefix = "FilmSearch.AI";

    /// <summary>The named HTTP client of a profile, e.g. <c>FilmSearch.AI.chat.local</c>: its own resilience pipeline, no request timeout.</summary>
    internal static string HttpClientName(AiProfileKind kind, string profileName) =>
        $"{HttpClientPrefix}.{(kind == AiProfileKind.Chat ? "chat" : "embedding")}.{profileName}";

    public IChatClient CreateChatClient(string profileName)
    {
        var ai = options.Value;
        var profile = ai.GetChatProfile(profileName);
        profileName = ai.ChatProfiles.Keys.First(k => string.Equals(k, profileName, StringComparison.OrdinalIgnoreCase));

        IChatClient providerClient = CreateOpenAIClient(profile, HttpClientName(AiProfileKind.Chat, profileName))
            .GetChatClient(profile.Model).AsIChatClient();

        return new ChatClientBuilder(providerClient)
            .ConfigureOptions(chatOptions =>
            {
                chatOptions.ModelId ??= profile.Model;
                chatOptions.Temperature ??= profile.Temperature;
                chatOptions.MaxOutputTokens ??= profile.MaxOutputTokens;
                if (profile.ReasoningEffort is { } effort)
                {
                    chatOptions.Reasoning ??= new ReasoningOptions { Effort = effort };
                }
            })
            .UseOpenTelemetry(loggerFactory, AiMetrics.SourceName, otel => otel.EnableSensitiveData = ai.EnableSensitiveData)
            .UseLogging(loggerFactory)
            .Use(inner => new UsageTrackingChatClient(
                inner, profileName, profile.Model, ai.FindPricing(profile.Model), metrics, loggerFactory.CreateLogger<UsageTrackingChatClient>()))
            .Build(services);
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(string profileName)
    {
        var ai = options.Value;
        var profile = ai.GetEmbeddingProfile(profileName);
        profileName = ai.EmbeddingProfiles.Keys.First(k => string.Equals(k, profileName, StringComparison.OrdinalIgnoreCase));

        var providerGenerator = CreateOpenAIClient(profile, HttpClientName(AiProfileKind.Embedding, profileName)).GetEmbeddingClient(profile.Model).AsIEmbeddingGenerator(profile.Dimensions);

        return new EmbeddingGeneratorBuilder<string, Embedding<float>>(providerGenerator)
            .UseOpenTelemetry(loggerFactory, AiMetrics.SourceName, otel => otel.EnableSensitiveData = ai.EnableSensitiveData)
            .UseLogging(loggerFactory)
            .Use(inner => new UsageTrackingEmbeddingGenerator(
                inner, profileName, profile.Model, ai.FindPricing(profile.Model), metrics, loggerFactory.CreateLogger<UsageTrackingEmbeddingGenerator>()))
            .Build(services);
    }

    private OpenAIClient CreateOpenAIClient(AiProfileOptions profile, string httpClientName)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(profile.Endpoint),
            // HTTP goes through IHttpClientFactory: the profile's resilience pipeline, OpenTelemetry HTTP spans and the
            // Development raw logging handler.
            Transport = new HttpClientPipelineTransport(httpClientFactory.CreateClient(httpClientName)),
            // Retries are in the HTTP pipeline, which knows which failures are safe to retry (AiHttpResilience).
            RetryPolicy = new ClientRetryPolicy(0),
            // A backstop for reading a streamed body; the attempt timeout of the pipeline fires first.
            NetworkTimeout = TimeSpan.FromSeconds(profile.TimeoutSeconds) + AiHttpResilience.SdkTimeoutMargin,
        };

        return new OpenAIClient(new ApiKeyCredential(profile.ApiKey ?? "unused"), clientOptions);
    }
}
