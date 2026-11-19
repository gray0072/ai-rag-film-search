using Microsoft.Extensions.AI;

namespace FilmSearch.AI.Providers;

/// <summary>
/// The <c>AI</c> configuration section (spec 06 §1). Chat and embeddings have separate named profiles, so chat can go
/// to OpenRouter while embeddings stay local. Switching a provider or model is a config change only (NFR-1).
/// Secrets (<c>ApiKey</c> of cloud profiles) live in user secrets or environment variables, never in appsettings.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "AI";

    /// <summary>Name of the chat profile behind the unkeyed <c>IChatClient</c>.</summary>
    public string ActiveChatProfile { get; set; } = "";

    /// <summary>Name of the embedding profile behind the unkeyed <c>IEmbeddingGenerator</c>.</summary>
    public string ActiveEmbeddingProfile { get; set; } = "";

    /// <summary>
    /// Chat profile that answers when the active one fails before it has streamed anything (provider down, timeout, open
    /// circuit; I19). Empty: no fallback. It must support tools, since the assistant gives it the same tools. There is
    /// no embedding fallback: vectors of another model are not comparable with the stored ones (search degrades to keyword).
    /// </summary>
    public string? FallbackChatProfile { get; set; }

    /// <summary>Query vectors are cached per profile, model and normalized query text (I19). 0 turns the cache off.</summary>
    public int QueryEmbeddingCacheMinutes { get; set; } = 24 * 60;

    /// <summary>Most cached query vectors (a 768-dimension vector is 3 KB).</summary>
    public int QueryEmbeddingCacheMaxEntries { get; set; } = 10_000;

    public Dictionary<string, ChatProfileOptions> ChatProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, EmbeddingProfileOptions> EmbeddingProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Price per million tokens by model id (the <c>Model</c> of a profile). Used for the estimated cost of each call.
    /// A model without an entry has no cost estimate. Configuration keys cannot contain ':', so a model id such as
    /// <c>vendor/model:free</c> cannot be priced here (it is free anyway).
    /// </summary>
    public Dictionary<string, ModelPricing> Pricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Record prompt and completion text in traces (OpenTelemetry GenAI events). On in Development only.</summary>
    public bool EnableSensitiveData { get; set; }

    /// <summary>
    /// Log raw HTTP requests and responses to the providers (Development only), to see what goes over the wire.
    /// API keys are never logged.
    /// </summary>
    public bool LogRawHttp { get; set; }

    /// <summary>Register health checks that the active providers are reachable and list the configured models.</summary>
    public bool HealthChecksEnabled { get; set; } = true;

    public ChatProfileOptions GetChatProfile(string name) =>
        ChatProfiles.TryGetValue(name, out var profile)
            ? profile
            : throw new InvalidOperationException($"AI chat profile '{name}' is not configured (AI:ChatProfiles).");

    public EmbeddingProfileOptions GetEmbeddingProfile(string name) =>
        EmbeddingProfiles.TryGetValue(name, out var profile)
            ? profile
            : throw new InvalidOperationException($"AI embedding profile '{name}' is not configured (AI:EmbeddingProfiles).");

    public ModelPricing? FindPricing(string model) => Pricing.GetValueOrDefault(model);

    /// <summary>The fallback chat profile name, or null when none is configured.</summary>
    public string? FallbackChatProfileName => string.IsNullOrWhiteSpace(FallbackChatProfile) ? null : FallbackChatProfile;
}

/// <summary>Connection settings shared by chat and embedding profiles: any OpenAI-compatible API.</summary>
public abstract class AiProfileOptions
{
    /// <summary>Base URL of the OpenAI-compatible API, e.g. <c>http://localhost:1234/v1</c>.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Model id as the provider knows it (LM Studio model key, OpenRouter <c>vendor/model</c>).</summary>
    public string Model { get; set; } = "";

    /// <summary>API key. LM Studio accepts any non-empty value; cloud keys come from user secrets.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Timeout of one attempt. Local models on a laptop are slow, so the default is generous.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Retries of failures where the request did not reach the model or was refused at once: connection errors, 429,
    /// 502, 503, 504. Timeouts and 500 are not retried (I19): the provider may still be working on the first request.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Base delay of the exponential retry backoff (with jitter); a <c>Retry-After</c> header wins.</summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Seconds the circuit breaker of this profile stays open after half of at least 3 attempts in a minute failed
    /// (connection errors, timeouts, 408, 429, 5xx). While it is open, calls fail at once and the fallback profile answers.
    /// </summary>
    public int CircuitBreakSeconds { get; set; } = 30;
}

public sealed class ChatProfileOptions : AiProfileOptions
{
    /// <summary>Default temperature when a call does not set one.</summary>
    public float? Temperature { get; set; }

    /// <summary>Default output token limit when a call does not set one.</summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// Default reasoning effort (<c>reasoning_effort</c> on the wire) for models that think before they answer.
    /// <c>None</c> turns thinking off: much lower latency for tool calls and extraction. Null leaves the model default.
    /// </summary>
    public ReasoningEffort? ReasoningEffort { get; set; }

    /// <summary>Whether the model can be given tools (the agent in I12 checks it).</summary>
    public bool SupportsTools { get; set; } = true;
}

public sealed class EmbeddingProfileOptions : AiProfileOptions
{
    public EmbeddingProfileOptions() => TimeoutSeconds = 60;

    /// <summary>Vector length the model returns. Stored with every vector (I06).</summary>
    public int Dimensions { get; set; }

    /// <summary>Text put before a search query. Many embedding models are trained with task prefixes.</summary>
    public string QueryPrefix { get; set; } = "";

    /// <summary>Text put before a document (movie text) when it is embedded.</summary>
    public string DocumentPrefix { get; set; } = "";

    public string FormatQuery(string query) => QueryPrefix + query;

    public string FormatDocument(string document) => DocumentPrefix + document;
}

/// <summary>USD per million tokens.</summary>
public sealed class ModelPricing
{
    public decimal InputPerMTokUsd { get; set; }

    public decimal OutputPerMTokUsd { get; set; }

    public decimal Estimate(long inputTokens, long outputTokens) =>
        ((inputTokens * InputPerMTokUsd) + (outputTokens * OutputPerMTokUsd)) / 1_000_000m;
}
