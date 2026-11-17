using System.Globalization;
using System.Text;
using System.Text.Json;
using FilmSearch.Core.Localization;
using FilmSearch.Evals.Metrics;
using FilmSearch.Evals.Suites;

namespace FilmSearch.Evals.Reports;

/// <summary>Aggregated query understanding metrics over a group of runs.</summary>
public sealed record QuSummary(
    int Cases,
    int Runs,
    double ValidRate,
    double RetryRate,
    double ExactMatchRate,
    IReadOnlyDictionary<string, double> FieldAccuracy,
    double GenresIncludePrecision,
    double GenresIncludeRecall,
    double GenresExcludePrecision,
    double GenresExcludeRecall,
    double LatencyP50Ms,
    double LatencyP95Ms)
{
    public static QuSummary Of(IReadOnlyList<QuCaseResult> results)
    {
        var pairs = results.SelectMany(r => r.Runs.Select(run => (r.Case, run))).ToList();
        var runs = pairs.Select(p => p.run).ToList();
        var (incP, incR) = PrecisionRecall(pairs.Select(p => (p.Case.Expected.GenresInclude, p.run.Understood.Filters.GenresInclude)));
        var (excP, excR) = PrecisionRecall(pairs.Select(p => (p.Case.Expected.GenresExclude, p.run.Understood.Filters.GenresExclude)));

        return new QuSummary(
            results.Count,
            runs.Count,
            Rate(runs, r => r.Valid),
            Rate(runs, r => r.Attempts > 1),
            Rate(runs, r => r.ExactMatch),
            QuSuite.FieldNames.ToDictionary(f => f, f => Rate(runs, r => r.Fields[f])),
            incP,
            incR,
            excP,
            excR,
            RetrievalMetrics.Percentile([.. runs.Select(r => r.LatencyMs)], 50),
            RetrievalMetrics.Percentile([.. runs.Select(r => r.LatencyMs)], 95));
    }

    private static double Rate(List<QuRun> runs, Func<QuRun, bool> predicate) =>
        runs.Count == 0 ? 0 : (double)runs.Count(predicate) / runs.Count;

    /// <summary>Micro precision and recall of genre slugs over all runs; 1 when there is nothing to predict or find.</summary>
    private static (double Precision, double Recall) PrecisionRecall(IEnumerable<(IReadOnlyList<string> Expected, IReadOnlyList<string> Actual)> pairs)
    {
        int truePositives = 0, predicted = 0, expected = 0;
        foreach (var (e, a) in pairs)
        {
            truePositives += a.Distinct().Count(e.Contains);
            predicted += a.Distinct().Count();
            expected += e.Count;
        }

        return (predicted == 0 ? 1 : (double)truePositives / predicted, expected == 0 ? 1 : (double)truePositives / expected);
    }
}

/// <summary>Markdown and JSON report of the query understanding suite.</summary>
public static class QuReport
{
    private static readonly JsonSerializerOptions CompactJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static object ToJson(RunHeader header, IReadOnlyList<QuCaseResult> results) => new
    {
        header,
        summary = QuSummary.Of(results),
        byLocale = Locales.All.ToDictionary(l => l, l => QuSummary.Of([.. results.Where(r => r.Case.Locale == l)])),
        cases = results.Select(r => new { id = r.Case.Id, r.Case.Query, expected = r.Case.Expected, runs = r.Runs }),
    };

    public static string ToMarkdown(RunHeader header, IReadOnlyList<QuCaseResult> results)
    {
        var s = QuSummary.Of(results);
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"# Query understanding evaluation: {header.Label} — {header.StartedAt:yyyy-MM-dd}");
        md.AppendLine();
        md.Append(header.ToMarkdown());
        md.AppendLine();
        md.AppendLine("Rates are over all runs (cases × repeat). Valid: the model gave a valid answer (no fallback). Retry: a second call");
        md.AppendLine("was needed. Exact match: every field right. A field is right when it equals the expected value; \"not mentioned\" must");
        md.AppendLine("stay empty, so an invented filter counts as wrong. Genre precision and recall are micro averages over slugs.");
        md.AppendLine();

        md.AppendLine("## Summary");
        md.AppendLine();
        md.AppendLine("| Valid | Retry | Exact match | Genres incl. P / R | Genres excl. P / R | p50 ms | p95 ms |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        md.AppendLine(CultureInfo.InvariantCulture,
            $"| {P(s.ValidRate)} | {P(s.RetryRate)} | {P(s.ExactMatchRate)} | {SearchReport.F(s.GenresIncludePrecision)} / {SearchReport.F(s.GenresIncludeRecall)} | {SearchReport.F(s.GenresExcludePrecision)} / {SearchReport.F(s.GenresExcludeRecall)} | {s.LatencyP50Ms:0} | {s.LatencyP95Ms:0} |");
        md.AppendLine();

        md.AppendLine("## Field accuracy");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"| Group | Cases | Valid | Exact | {string.Join(" | ", QuSuite.FieldNames)} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"|---|---|---|---|{string.Concat(QuSuite.FieldNames.Select(_ => "---|"))}");
        AppendRow(md, "all", s);
        foreach (var locale in Locales.All)
        {
            AppendRow(md, locale, QuSummary.Of([.. results.Where(r => r.Case.Locale == locale)]));
        }

        md.AppendLine();
        AppendMismatches(md, results);

        md.AppendLine("## Conclusion");
        md.AppendLine();
        md.AppendLine("_Write after reading the numbers: what changed, why, and what to try next._");
        return md.ToString();
    }

    private static void AppendRow(StringBuilder md, string group, QuSummary s) =>
        md.AppendLine(CultureInfo.InvariantCulture,
            $"| {group} | {s.Cases} | {P(s.ValidRate)} | {P(s.ExactMatchRate)} | {string.Join(" | ", QuSuite.FieldNames.Select(f => P(s.FieldAccuracy[f])))} |");

    /// <summary>Cases with a wrong field in any run, with the answer of the first such run.</summary>
    private static void AppendMismatches(StringBuilder md, IReadOnlyList<QuCaseResult> results)
    {
        var mismatches = results
            .Select(r => (r.Case, Run: r.Runs.FirstOrDefault(run => !run.ExactMatch), Wrong: r.Runs.Count(run => !run.ExactMatch), r.Runs.Count))
            .Where(x => x.Run is not null)
            .ToList();

        md.AppendLine(CultureInfo.InvariantCulture, $"## Mismatches ({mismatches.Count} of {results.Count} cases)");
        md.AppendLine();
        if (mismatches.Count == 0)
        {
            md.AppendLine("None.");
            md.AppendLine();
            return;
        }

        md.AppendLine("| Case | Query | Wrong runs | Wrong fields | Answer |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var (quCase, run, wrong, count) in mismatches)
        {
            var fields = run!.Valid
                ? string.Join(", ", run.Fields.Where(f => !f.Value).Select(f => f.Key))
                : $"fallback: {run.Understood.Error}";
            var answer = JsonSerializer.Serialize(new { run.Understood.Filters, run.Understood.SimilarToTitles, run.Understood.SemanticQuery }, CompactJson);
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {quCase.Id} | {SearchReport.Escape(quCase.Query)} | {wrong}/{count} | {SearchReport.Escape(fields)} | `{SearchReport.Escape(answer)}` |");
        }

        md.AppendLine();
    }

    private static string P(double rate) => rate.ToString("0%", CultureInfo.InvariantCulture);
}
