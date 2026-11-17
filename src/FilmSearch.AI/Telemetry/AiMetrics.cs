using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FilmSearch.AI.Telemetry;

/// <summary>
/// Names and custom instruments of AI telemetry. Microsoft.Extensions.AI emits the standard GenAI spans and
/// metrics (<c>gen_ai.client.token.usage</c>, <c>gen_ai.client.operation.duration</c>) under <see cref="SourceName"/>;
/// the counters below (meter <c>FilmSearch</c>, spec 10 §3) add the profile and the estimated cost.
/// </summary>
public sealed class AiMetrics
{
    /// <summary>Activity source and meter of the Microsoft.Extensions.AI OpenTelemetry middleware.</summary>
    public const string SourceName = "FilmSearch.AI";

    /// <summary>Meter of custom FilmSearch metrics (same name as in ServiceDefaults).</summary>
    public const string MeterName = "FilmSearch";

    /// <summary>Span tag with the estimated cost of the call in USD.</summary>
    public const string CostTag = "filmsearch.ai.cost_usd";

    /// <summary>Span tag with the profile name.</summary>
    public const string ProfileTag = "filmsearch.ai.profile";

    private readonly Counter<long> _tokens;
    private readonly Counter<double> _cost;

    public AiMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _tokens = meter.CreateCounter<long>("ai.tokens", unit: "{token}", description: "Tokens sent to and received from AI models.");
        _cost = meter.CreateCounter<double>("ai.cost_usd", unit: "USD", description: "Estimated cost of AI calls.");
    }

    internal void Record(AiUsage usage)
    {
        var model = new KeyValuePair<string, object?>("model", usage.Model);
        var profile = new KeyValuePair<string, object?>("profile", usage.Profile);
        var operation = new KeyValuePair<string, object?>("operation", usage.Operation);

        if (usage.InputTokens > 0)
        {
            _tokens.Add(usage.InputTokens, model, profile, operation, new("direction", "input"));
        }

        if (usage.OutputTokens > 0)
        {
            _tokens.Add(usage.OutputTokens, model, profile, operation, new("direction", "output"));
        }

        if (usage.EstimatedCostUsd is { } cost)
        {
            _cost.Add((double)cost, model, profile, operation);
        }

        // The GenAI span of the OpenTelemetry middleware (outer in the pipeline) is the current activity here.
        if (Activity.Current is { } activity && activity.Source.Name == SourceName)
        {
            activity.SetTag(ProfileTag, usage.Profile);
            if (usage.EstimatedCostUsd is { } spanCost)
            {
                activity.SetTag(CostTag, (double)spanCost);
            }
        }
    }
}

/// <summary>Token usage and estimated cost of one AI call.</summary>
public sealed record AiUsage(string Profile, string Model, string Operation, long InputTokens, long OutputTokens, decimal? EstimatedCostUsd)
{
    /// <summary>Key of the <see cref="AiUsage"/> in <c>ChatResponse.AdditionalProperties</c> and <c>GeneratedEmbeddings.AdditionalProperties</c>.</summary>
    public const string PropertyKey = "filmsearch.usage";
}
