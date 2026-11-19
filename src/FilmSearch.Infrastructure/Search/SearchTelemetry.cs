using System.Diagnostics;
using System.Diagnostics.Metrics;
using FilmSearch.Core.Search;

namespace FilmSearch.Infrastructure.Search;

/// <summary>
/// Spans of the search stages (spec 10 §3): <c>search {mode}</c> around a request, with <c>search.understand</c>,
/// <c>search.keyword</c>, <c>search.embed_query</c>, <c>search.vector</c> and <c>search.fusion</c> inside. Query text is
/// not recorded (user input); counts, match kind and the degraded flag are.
/// </summary>
internal static class SearchTelemetry
{
    /// <summary>Activity source and meter of custom FilmSearch telemetry (the name ServiceDefaults listens to).</summary>
    public const string Name = "FilmSearch";

    public static readonly ActivitySource Activities = new(Name);
}

/// <summary>Search metrics (meter <c>FilmSearch</c>, spec 10 §3).</summary>
public sealed class SearchMetrics
{
    private readonly Counter<long> _requests;
    private readonly Histogram<double> _latency;

    public SearchMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(SearchTelemetry.Name);
        _requests = meter.CreateCounter<long>(
            "search.requests", unit: "{request}", description: "Search requests by mode; degraded=true when semantic or hybrid search fell back to keyword results.");
        _latency = meter.CreateHistogram<double>(
            "search.latency",
            unit: "s",
            description: "Duration of a search request, query understanding and loading the movies included.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10] });
    }

    internal void Record(SearchMode mode, bool understand, bool degraded, TimeSpan duration)
    {
        KeyValuePair<string, object?>[] tags =
        [
            new("mode", ModeName(mode)),
            new("understand", understand),
            new("degraded", degraded),
        ];
        _requests.Add(1, tags);
        _latency.Record(duration.TotalSeconds, tags);
    }

    internal static string ModeName(SearchMode mode) => mode switch
    {
        SearchMode.Keyword => "keyword",
        SearchMode.Semantic => "semantic",
        SearchMode.Hybrid => "hybrid",
        _ => mode.ToString().ToLowerInvariant(),
    };
}
