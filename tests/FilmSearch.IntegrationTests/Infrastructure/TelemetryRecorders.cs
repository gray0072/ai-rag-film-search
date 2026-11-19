using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FilmSearch.AI.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>One measurement of an instrument with its tags.</summary>
public sealed record RecordedMeasurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags)
{
    public object? Tag(string name) => Tags.GetValueOrDefault(name);
}

/// <summary>
/// Measurements of the instruments created by one app's <see cref="IMeterFactory"/> (meters of a factory carry it as
/// their scope), so tests of other factories running in parallel do not count.
/// </summary>
public sealed class MeterRecorder : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<RecordedMeasurement> _measurements = new();

    public MeterRecorder(IServiceProvider services, params string[] instruments)
    {
        var factory = services.GetRequiredService<IMeterFactory>();
        var names = instruments.ToHashSet(StringComparer.Ordinal);
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, factory) && names.Contains(instrument.Name))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((i, value, tags, _) => Add(i, value, tags));
        _listener.SetMeasurementEventCallback<double>((i, value, tags, _) => Add(i, value, tags));
        _listener.Start();
    }

    public IReadOnlyList<RecordedMeasurement> Of(string instrument) => [.. _measurements.Where(m => m.Instrument == instrument)];

    public void Dispose() => _listener.Dispose();

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        _measurements.Enqueue(new RecordedMeasurement(instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));
}

/// <summary>Stopped spans of one trace (the test sends its own <c>traceparent</c>), from every activity source.</summary>
public sealed class SpanRecorder : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _spans = new();

    public SpanRecorder()
    {
        TraceId = ActivityTraceId.CreateRandom();
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == TraceId)
                {
                    _spans.Enqueue(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public ActivityTraceId TraceId { get; }

    /// <summary>W3C <c>traceparent</c> header value that puts the server spans into <see cref="TraceId"/>.</summary>
    public string TraceParent => $"00-{TraceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01";

    public IReadOnlyList<Activity> Spans => [.. _spans];

    public void Dispose() => _listener.Dispose();
}

/// <summary>Records the scores the API would send to Langfuse.</summary>
public sealed class RecordingFeedbackScoreSink : IFeedbackScoreSink
{
    private readonly ConcurrentQueue<FeedbackScore> _scores = new();

    public IReadOnlyList<FeedbackScore> Scores => [.. _scores];

    public void Enqueue(FeedbackScore score) => _scores.Enqueue(score);
}
