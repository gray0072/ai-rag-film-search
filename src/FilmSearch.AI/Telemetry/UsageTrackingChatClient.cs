using System.Runtime.CompilerServices;
using FilmSearch.AI.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FilmSearch.AI.Telemetry;

/// <summary>
/// Pipeline step that records token usage and estimated cost of every chat call (NFR-5): metrics, a span tag, one
/// structured log line, and an <see cref="AiUsage"/> in <c>ChatResponse.AdditionalProperties</c> so callers (the chat
/// module in I11) can store it per conversation.
/// </summary>
internal sealed partial class UsageTrackingChatClient(
    IChatClient innerClient,
    string profile,
    string model,
    ModelPricing? pricing,
    AiMetrics metrics,
    ILogger logger) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        var usage = Track(response.Usage, response.ModelId, "chat");
        (response.AdditionalProperties ??= [])[AiUsage.PropertyKey] = usage;

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Providers send usage in the last chunk (OpenAI: stream_options.include_usage) as UsageContent.
        UsageDetails? usageDetails = null;
        string? responseModel = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            responseModel ??= update.ModelId;
            foreach (var usage in update.Contents.OfType<UsageContent>())
            {
                (usageDetails ??= new UsageDetails()).Add(usage.Details);
            }

            yield return update;
        }

        Track(usageDetails, responseModel, "chat_stream");
    }

    private AiUsage Track(UsageDetails? details, string? responseModel, string operation)
    {
        var input = details?.InputTokenCount ?? 0;
        var output = details?.OutputTokenCount ?? 0;
        var usage = new AiUsage(profile, model, operation, input, output, pricing?.Estimate(input, output));

        metrics.Record(usage);
        LogUsage(logger, operation, profile, responseModel ?? model, input, output, usage.EstimatedCostUsd);

        return usage;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "AI {Operation} via profile {Profile}, model {Model}: {InputTokens} input tokens, {OutputTokens} output tokens, estimated cost {CostUsd} USD")]
    private static partial void LogUsage(
        ILogger logger, string operation, string profile, string model, long inputTokens, long outputTokens, decimal? costUsd);
}
