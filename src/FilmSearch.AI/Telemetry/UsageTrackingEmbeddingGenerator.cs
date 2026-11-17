using FilmSearch.AI.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FilmSearch.AI.Telemetry;

/// <summary>Records token usage and estimated cost of every embedding call (see <see cref="UsageTrackingChatClient"/>).</summary>
internal sealed partial class UsageTrackingEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> innerGenerator,
    string profile,
    string model,
    ModelPricing? pricing,
    AiMetrics metrics,
    ILogger logger) : DelegatingEmbeddingGenerator<string, Embedding<float>>(innerGenerator)
{
    public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var embeddings = await base.GenerateAsync(values, options, cancellationToken);

        var input = embeddings.Usage?.InputTokenCount ?? 0;
        var usage = new AiUsage(profile, model, "embeddings", input, 0, pricing?.Estimate(input, 0));
        metrics.Record(usage);
        (embeddings.AdditionalProperties ??= [])[AiUsage.PropertyKey] = usage;
        LogUsage(logger, embeddings.Count, profile, model, input, usage.EstimatedCostUsd);

        return embeddings;
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "AI embeddings via profile {Profile}, model {Model}: {Count} vectors, {InputTokens} input tokens, estimated cost {CostUsd} USD")]
    private static partial void LogUsage(ILogger logger, int count, string profile, string model, long inputTokens, decimal? costUsd);
}
