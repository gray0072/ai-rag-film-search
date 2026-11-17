using System.ClientModel;
using System.ClientModel.Primitives;
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
/// GenAI span → logging → usage and cost tracking → provider client (timeout and retries of the profile) → HTTP.
/// </para>
/// </summary>
internal sealed class AiClientFactory(
    IOptions<AiOptions> options,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    AiMetrics metrics,
    IServiceProvider services)
{
    /// <summary>Named HTTP client for provider calls: no resilience handler and no request timeout (see AddAiProviders).</summary>
    internal const string HttpClientName = "FilmSearch.AI";

    public IChatClient CreateChatClient(string profileName)
    {
        var ai = options.Value;
        var profile = ai.GetChatProfile(profileName);

        IChatClient providerClient = CreateOpenAIClient(profile).GetChatClient(profile.Model).AsIChatClient();

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

        var providerGenerator = CreateOpenAIClient(profile).GetEmbeddingClient(profile.Model).AsIEmbeddingGenerator(profile.Dimensions);

        return new EmbeddingGeneratorBuilder<string, Embedding<float>>(providerGenerator)
            .UseOpenTelemetry(loggerFactory, AiMetrics.SourceName, otel => otel.EnableSensitiveData = ai.EnableSensitiveData)
            .UseLogging(loggerFactory)
            .Use(inner => new UsageTrackingEmbeddingGenerator(
                inner, profileName, profile.Model, ai.FindPricing(profile.Model), metrics, loggerFactory.CreateLogger<UsageTrackingEmbeddingGenerator>()))
            .Build(services);
    }

    private OpenAIClient CreateOpenAIClient(AiProfileOptions profile)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(profile.Endpoint),
            // HTTP goes through IHttpClientFactory: OpenTelemetry HTTP spans and the Development raw logging handler.
            Transport = new HttpClientPipelineTransport(httpClientFactory.CreateClient(HttpClientName)),
            // The SDK retry policy: exponential backoff on 408/429/5xx and network errors, honours Retry-After.
            RetryPolicy = new ClientRetryPolicy(profile.MaxRetries),
            NetworkTimeout = TimeSpan.FromSeconds(profile.TimeoutSeconds),
        };

        return new OpenAIClient(new ApiKeyCredential(profile.ApiKey ?? "unused"), clientOptions);
    }
}
