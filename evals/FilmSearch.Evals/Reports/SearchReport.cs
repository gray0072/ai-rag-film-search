using System.Globalization;
using System.Text;
using FilmSearch.Core.Localization;
using FilmSearch.Core.Search;
using FilmSearch.Evals.Datasets;
using FilmSearch.Evals.Metrics;
using FilmSearch.Evals.Suites;

namespace FilmSearch.Evals.Reports;

/// <summary>Aggregated metrics of one mode over a group of cases (all, one locale or one tag).</summary>
public sealed record SearchSummary(
    string Mode,
    int Cases,
    double Recall5,
    double Recall10,
    double Mrr,
    double Ndcg10,
    double? ExcludedGenreRate,
    double LatencyP50Ms,
    double LatencyP95Ms,
    int Degraded,
    int QuFallbacks)
{
    public static SearchSummary Of(string mode, IReadOnlyList<SearchCaseResult> results)
    {
        var runs = results.SelectMany(r => r.Runs).ToList();
        var latencies = runs.Select(r => r.LatencyMs).ToList();
        var violations = results.Select(r => r.ViolationRate).OfType<double>().ToList();

        return new SearchSummary(
            mode,
            results.Count,
            Mean(results.Select(r => r.Recall5)),
            Mean(results.Select(r => r.Recall10)),
            Mean(results.Select(r => r.Mrr)),
            Mean(results.Select(r => r.Ndcg10)),
            violations.Count > 0 ? violations.Average() : null,
            RetrievalMetrics.Percentile(latencies, 50),
            RetrievalMetrics.Percentile(latencies, 95),
            runs.Count(r => r.Degraded),
            runs.Count(r => r.Understood?.Source == QueryUnderstandingSource.Fallback));
    }

    private static double Mean(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : list.Average();
    }
}

/// <summary>Markdown and JSON report of the search suite.</summary>
public static class SearchReport
{
    public static object ToJson(RunHeader header, IReadOnlyList<SearchCaseResult> results, IReadOnlyList<string> modes) => new
    {
        header,
        summary = modes.Select(m => SearchSummary.Of(m, [.. results.Where(r => r.Mode == m)])),
        byLocale = Locales.All.ToDictionary(l => l, l => modes.Select(m => SearchSummary.Of(m, [.. results.Where(r => r.Mode == m && r.Case.Locale == l)]))),
        byTag = SearchTags.All.ToDictionary(t => t, t => modes.Select(m => SearchSummary.Of(m, [.. results.Where(r => r.Mode == m && r.Case.Tags.Contains(t))]))),
        cases = results.Select(r => new
        {
            id = r.Case.Id,
            mode = r.Mode,
            r.Recall5,
            r.Recall10,
            r.Mrr,
            r.Ndcg10,
            r.ViolationRate,
            runs = r.Runs,
        }),
    };

    public static string ToMarkdown(RunHeader header, IReadOnlyList<SearchCaseResult> results, IReadOnlyList<string> modes, SeedCatalog seed)
    {
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"# Search evaluation: {header.Label} — {header.StartedAt:yyyy-MM-dd}");
        md.AppendLine();
        md.Append(header.ToMarkdown());
        md.AppendLine();
        md.AppendLine("Metrics are means over cases (spec 09 §3). Recall@k = relevant movies in the top k / min(relevant, k). MRR and nDCG");
        md.AppendLine("use the top 10; nDCG uses the grades of the dataset (1–3, gain 2^grade − 1). Excluded-genre rate: share of the top 10");
        md.AppendLine("with a genre the query rules out (cases with `excludedGenres` only). Latency: one `IMovieSearch.SearchAsync` call");
        md.AppendLine("in process (embedding, SQL, loading movies; query understanding in `hybrid+qu`), without HTTP.");
        md.AppendLine();

        md.AppendLine("## Summary");
        md.AppendLine();
        md.AppendLine("| Mode | Recall@5 | Recall@10 | MRR@10 | nDCG@10 | Excluded-genre rate | p50 ms | p95 ms | Degraded | QU fallbacks |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var mode in modes)
        {
            var s = SearchSummary.Of(mode, [.. results.Where(r => r.Mode == mode)]);
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {mode} | {F(s.Recall5)} | {F(s.Recall10)} | {F(s.Mrr)} | {F(s.Ndcg10)} | {(s.ExcludedGenreRate is { } v ? F(v) : "–")} | {s.LatencyP50Ms:0} | {s.LatencyP95Ms:0} | {s.Degraded} | {s.QuFallbacks} |");
        }

        md.AppendLine();
        AppendBreakdown(md, "By locale (Recall@10 / MRR@10)", Locales.All, (r, l) => r.Case.Locale == l, results, modes);
        AppendBreakdown(md, "By tag (Recall@10 / MRR@10)", SearchTags.All, (r, t) => r.Case.Tags.Contains(t), results, modes);

        AppendMisses(md, results, modes, seed);
        AppendPerCase(md, results, modes);

        md.AppendLine("## Conclusion");
        md.AppendLine();
        md.AppendLine("_Write after reading the numbers: what changed, why, and what to try next._");
        return md.ToString();
    }

    private static void AppendBreakdown(
        StringBuilder md,
        string title,
        IReadOnlyList<string> groups,
        Func<SearchCaseResult, string, bool> belongs,
        IReadOnlyList<SearchCaseResult> results,
        IReadOnlyList<string> modes)
    {
        md.AppendLine(CultureInfo.InvariantCulture, $"## {title}");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"| Group | Cases | {string.Join(" | ", modes)} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"|---|---|{string.Concat(modes.Select(_ => "---|"))}");
        foreach (var group in groups)
        {
            var cells = modes.Select(m =>
            {
                var s = SearchSummary.Of(m, [.. results.Where(r => r.Mode == m && belongs(r, group))]);
                return s.Cases == 0 ? "–" : $"{F(s.Recall10)} / {F(s.Mrr)}";
            });
            var count = results.Where(r => r.Mode == modes[0] && belongs(r, group)).Count();
            md.AppendLine(CultureInfo.InvariantCulture, $"| {group} | {count} | {string.Join(" | ", cells)} |");
        }

        md.AppendLine();
    }

    /// <summary>Cases where no mode found a relevant movie in the top 10, with the top 3 of the last mode.</summary>
    private static void AppendMisses(StringBuilder md, IReadOnlyList<SearchCaseResult> results, IReadOnlyList<string> modes, SeedCatalog seed)
    {
        var lastMode = modes[^1];
        var misses = results
            .GroupBy(r => r.Case.Id)
            .Where(g => g.All(r => r.Recall10 == 0))
            .Select(g => g.Single(r => r.Mode == lastMode))
            .ToList();

        md.AppendLine(CultureInfo.InvariantCulture, $"## Misses of every mode ({misses.Count})");
        md.AppendLine();
        if (misses.Count == 0)
        {
            md.AppendLine("None.");
            md.AppendLine();
            return;
        }

        md.AppendLine(CultureInfo.InvariantCulture, $"| Case | Query | Expected | Top 3 of {lastMode} |");
        md.AppendLine("|---|---|---|---|");
        foreach (var miss in misses)
        {
            var expected = string.Join("; ", miss.Case.Relevant.Take(3).Select(seed.TitleOf)) + (miss.Case.Relevant.Count > 3 ? "; …" : "");
            var top = string.Join("; ", miss.Runs[^1].Results.Take(3).Select(seed.TitleOf));
            md.AppendLine(CultureInfo.InvariantCulture, $"| {miss.Case.Id} | {Escape(miss.Case.Query)} | {Escape(expected)} | {Escape(top)} |");
        }

        md.AppendLine();
    }

    /// <summary>Rank of the first relevant movie per case and mode: the table to diff between two reports.</summary>
    private static void AppendPerCase(StringBuilder md, IReadOnlyList<SearchCaseResult> results, IReadOnlyList<string> modes)
    {
        md.AppendLine("## Per case");
        md.AppendLine();
        md.AppendLine("Rank of the first relevant movie in the last run (– = not in the top 10), then Recall@10 of the case.");
        md.AppendLine();
        md.AppendLine("<details><summary>All cases</summary>");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"| Case | Tags | {string.Join(" | ", modes)} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"|---|---|{string.Concat(modes.Select(_ => "---|"))}");
        foreach (var group in results.GroupBy(r => r.Case.Id))
        {
            var searchCase = group.First().Case;
            var grades = searchCase.Grades();
            var cells = modes.Select(m =>
            {
                var result = group.Single(r => r.Mode == m);
                var rank = FirstRelevantRank(result.Runs[^1].Results, grades);
                return $"{rank?.ToString(CultureInfo.InvariantCulture) ?? "–"} ({F(result.Recall10)})";
            });
            md.AppendLine(CultureInfo.InvariantCulture, $"| {searchCase.Id} | {string.Join(", ", searchCase.Tags)} | {string.Join(" | ", cells)} |");
        }

        md.AppendLine();
        md.AppendLine("</details>");
        md.AppendLine();
    }

    private static int? FirstRelevantRank(IReadOnlyList<int> ranked, IReadOnlyDictionary<int, int> grades)
    {
        for (var i = 0; i < ranked.Count; i++)
        {
            if (grades.ContainsKey(ranked[i]))
            {
                return i + 1;
            }
        }

        return null;
    }

    internal static string F(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);

    internal static string Escape(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
