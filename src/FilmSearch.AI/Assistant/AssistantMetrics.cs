using System.Diagnostics.Metrics;
using FilmSearch.AI.Telemetry;

namespace FilmSearch.AI.Assistant;

/// <summary>
/// Metrics of the assistant (meter <c>FilmSearch</c>, spec 10 §3). Together with the GenAI metrics of every model call
/// they answer: how many turns end well, how long they take, which tools fail, how often the loop hits its limit and how
/// often an answer names a movie that no tool returned.
/// </summary>
public sealed class AssistantMetrics
{
    /// <summary>Outcome of a turn that wrote an answer (the other outcomes are <see cref="FilmSearch.Core.Assistant.AssistantErrorCodes"/>).</summary>
    public const string Answered = "answered";

    /// <summary>Outcome of a turn the client stopped (disconnect or the stop button).</summary>
    public const string Cancelled = "cancelled";

    private readonly Counter<long> _turns;
    private readonly Histogram<double> _turnDuration;
    private readonly Counter<long> _toolCalls;
    private readonly Counter<long> _iterationLimitHits;
    private readonly Counter<long> _groundingViolations;
    private readonly Counter<long> _titles;
    private readonly Counter<long> _feedback;

    public AssistantMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(AiMetrics.MeterName);
        _turns = meter.CreateCounter<long>(
            "assistant.turns", unit: "{turn}", description: "Assistant turns by outcome: answered, cancelled or an error code.");
        _turnDuration = meter.CreateHistogram<double>(
            "assistant.turn.duration",
            unit: "s",
            description: "Time from the user message to the end of the answer (all model calls and tools of the turn).",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.5, 1, 2, 5, 10, 20, 30, 60, 120, 300] });
        _toolCalls = meter.CreateCounter<long>(
            "assistant.tool_calls", unit: "{call}", description: "Tool calls of the agent loop; ok=false when the model got an error result.");
        _iterationLimitHits = meter.CreateCounter<long>(
            "assistant.iteration_limit_hits", unit: "{turn}", description: "Turns that used all tool iterations and had to answer without tools.");
        _groundingViolations = meter.CreateCounter<long>(
            "assistant.grounding_violations", unit: "{answer}", description: "Answers that name at least one movie no tool returned (possible hallucination).");
        _titles = meter.CreateCounter<long>(
            "assistant.grounding.titles",
            unit: "{title}",
            description: "Movie titles named in assistant answers; grounded=false when no tool returned the movie (possible hallucination).");
        _feedback = meter.CreateCounter<long>(
            "assistant.feedback", unit: "{rating}", description: "Ratings of answers by users: rating=up or down.");
    }

    /// <summary>One turn ended. <paramref name="outcome"/>: <see cref="Answered"/>, <see cref="Cancelled"/> or an error code.</summary>
    internal void RecordTurn(string outcome, string? promptId, TimeSpan duration)
    {
        KeyValuePair<string, object?>[] tags = [new("outcome", outcome), new("prompt", promptId ?? "none")];
        _turns.Add(1, tags);
        _turnDuration.Record(duration.TotalSeconds, tags);
    }

    internal void RecordToolCall(string tool, bool ok) =>
        _toolCalls.Add(1, new KeyValuePair<string, object?>("tool", tool), new("ok", ok));

    internal void RecordIterationLimitHit(string? promptId) =>
        _iterationLimitHits.Add(1, new KeyValuePair<string, object?>("prompt", promptId ?? "none"));

    internal void RecordGrounding(GroundingResult result, string promptId)
    {
        var prompt = new KeyValuePair<string, object?>("prompt", promptId);
        var grounded = result.Titles.Count - result.Unknown.Count;
        if (grounded > 0)
        {
            _titles.Add(grounded, prompt, new("grounded", true));
        }

        if (result.Unknown.Count > 0)
        {
            _titles.Add(result.Unknown.Count, prompt, new("grounded", false));
            _groundingViolations.Add(1, prompt);
        }
    }

    /// <summary>A user rated an answer (online quality signal; the same rating goes to Langfuse as a score when it is on).</summary>
    public void RecordFeedback(int rating) =>
        _feedback.Add(1, new KeyValuePair<string, object?>("rating", rating > 0 ? "up" : "down"));
}
