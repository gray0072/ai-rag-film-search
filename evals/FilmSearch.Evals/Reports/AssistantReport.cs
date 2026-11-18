using System.Globalization;
using System.Text;
using FilmSearch.Evals.Assistant;
using FilmSearch.Evals.Metrics;

namespace FilmSearch.Evals.Reports;

/// <summary>A mean and its standard deviation over the repeats (the run-to-run variance of an LLM suite).</summary>
public sealed record Spread(double Mean, double StdDev)
{
    public static Spread Of(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return new Spread(0, 0);
        }

        var mean = values.Average();
        var variance = values.Count > 1 ? values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1) : 0;
        return new Spread(mean, Math.Sqrt(variance));
    }
}

/// <summary>Aggregated assistant metrics over a group of case results.</summary>
public sealed record AssistantSummary(
    int Cases,
    int Runs,
    int Turns,
    Spread PassRate,
    int StableCases,
    int FlakyCases,
    int FailingCases,
    IReadOnlyDictionary<string, (int Applied, double PassRate)> Checks,
    double GroundingViolationRate,
    int Titles,
    int UnknownTitles,
    IReadOnlyDictionary<string, Spread> Judge,
    Spread JudgeMean,
    int JudgeFailures,
    double InputTokensPerTurn,
    double OutputTokensPerTurn,
    long InputTokens,
    long OutputTokens,
    double ModelCallsPerTurn,
    int IterationLimitHits,
    int ProviderErrors,
    double LatencyP50Ms,
    double LatencyP95Ms)
{
    public static AssistantSummary Of(IReadOnlyList<AssistantCaseResult> results)
    {
        var runs = results.SelectMany(r => r.Runs).ToList();
        var turns = runs.SelectMany(r => r.Turns).ToList();
        var repeats = runs.Select(r => r.Repeat).Distinct().Order().ToList();

        // Pass rate per repeat (share of cases that passed in that repeat), then mean ± stdev over the repeats.
        var passRate = Spread.Of([.. repeats.Select(rep => Rate(results, r => r.Runs.Where(x => x.Repeat == rep).All(x => x.Passed)))]);

        var checks = TurnChecks.All.ToDictionary(
            name => name,
            name =>
            {
                var applied = turns.SelectMany(t => t.Checks).Where(c => c.Name == name).ToList();
                return (applied.Count, applied.Count == 0 ? 0 : (double)applied.Count(c => c.Passed) / applied.Count);
            });

        var withTitles = turns.Where(t => t.Titles.Count > 0).ToList();
        var judged = runs.Where(r => r.Judge is not null).ToList();
        var judge = JudgeScores.Criteria.ToDictionary(
            c => c,
            c => Spread.Of([.. repeats.Select(rep => judged.Where(r => r.Repeat == rep).Select(r => (double)r.Judge!.Get(c)).DefaultIfEmpty(0).Average())]));
        var judgeMean = Spread.Of([.. repeats.Select(rep => judged.Where(r => r.Repeat == rep).Select(r => r.Judge!.Mean).DefaultIfEmpty(0).Average())]);
        var latencies = turns.Where(t => t.LatencyMs is not null).Select(t => (double)t.LatencyMs!.Value).ToArray();

        return new AssistantSummary(
            results.Count,
            runs.Count,
            turns.Count,
            passRate,
            results.Count(r => r.Runs.All(x => x.Passed)),
            results.Count(r => r.Runs.Any(x => x.Passed) && !r.Runs.All(x => x.Passed)),
            results.Count(r => !r.Runs.Any(x => x.Passed)),
            checks,
            withTitles.Count == 0 ? 0 : (double)withTitles.Count(t => t.UnknownTitles.Count > 0) / withTitles.Count,
            turns.Sum(t => t.Titles.Count),
            turns.Sum(t => t.UnknownTitles.Count),
            judged.Count == 0 ? new Dictionary<string, Spread>() : judge,
            judgeMean,
            runs.Count(r => r.JudgeError is not null),
            turns.Count == 0 ? 0 : turns.Average(t => t.InputTokens),
            turns.Count == 0 ? 0 : turns.Average(t => t.OutputTokens),
            turns.Sum(t => t.InputTokens),
            turns.Sum(t => t.OutputTokens),
            turns.Count == 0 ? 0 : turns.Average(t => t.ModelCalls),
            turns.Count(t => t.IterationLimitHit),
            runs.Count(r => r.ProviderError),
            latencies.Length == 0 ? 0 : RetrievalMetrics.Percentile(latencies, 50),
            latencies.Length == 0 ? 0 : RetrievalMetrics.Percentile(latencies, 95));
    }

    private static double Rate(IReadOnlyList<AssistantCaseResult> results, Func<AssistantCaseResult, bool> predicate) =>
        results.Count == 0 ? 0 : (double)results.Count(predicate) / results.Count;
}

/// <summary>Markdown and JSON reports of the assistant suites.</summary>
public static class AssistantReport
{
    private const int ExcerptLength = 400;

    public static string ToMarkdown(RunHeader header, IReadOnlyList<AssistantCaseResult> results, decimal? estimatedCostUsd)
    {
        var md = new StringBuilder();
        var title = header.Suite == EvalOptions.AdversarialSuite ? "Assistant adversarial evaluation" : "Assistant scenario evaluation";
        md.AppendLine(CultureInfo.InvariantCulture, $"# {title}: {header.Label}");
        md.AppendLine();
        md.Append(header.ToMarkdown());
        md.AppendLine();

        var all = AssistantSummary.Of(results);
        md.AppendLine("## Summary");
        md.AppendLine();
        md.AppendLine("A case passes when every check of every turn passes. Pass rate = share of cases that passed, per repeat; mean ± standard deviation over the repeats.");
        md.AppendLine();
        md.AppendLine("| Metric | Value |");
        md.AppendLine("|---|---|");
        Row("Cases / runs / turns", $"{all.Cases} / {all.Runs} / {all.Turns}");
        Row(header.Suite == EvalOptions.AdversarialSuite ? "**Adversarial pass rate**" : "**Pass rate**", $"**{Percent(all.PassRate)}**");
        Row("Cases: pass every repeat / flaky / fail every repeat", $"{all.StableCases} / {all.FlakyCases} / {all.FailingCases}");
        Row("**Grounding violation rate** (turns naming a movie no tool returned / turns naming movies)", $"**{all.GroundingViolationRate * 100:0.0}%** ({all.UnknownTitles} of {all.Titles} titles)");
        if (all.Judge.Count > 0)
        {
            Row("Judge mean (1–5)", Score(all.JudgeMean));
            foreach (var (criterion, spread) in all.Judge)
            {
                Row($"Judge: {criterion}", Score(spread));
            }
        }

        if (all.JudgeFailures > 0)
        {
            Row("Judge failures (invalid output)", Format(all.JudgeFailures));
        }

        Row("Tokens per turn (input / output)", $"{all.InputTokensPerTurn:0} / {all.OutputTokensPerTurn:0}");
        Row("Tokens in total (input / output)", $"{all.InputTokens} / {all.OutputTokens}");
        if (estimatedCostUsd is { } cost)
        {
            Row("Estimated cost of the assistant (without the judge)", $"${cost:0.0000}");
        }

        Row("Model calls per turn", $"{all.ModelCallsPerTurn:0.00}");
        Row("Turn latency p50 / p95", $"{all.LatencyP50Ms / 1000:0.0} s / {all.LatencyP95Ms / 1000:0.0} s");
        Row("Iteration limit hits", Format(all.IterationLimitHits));
        Row("Runs with a provider error", Format(all.ProviderErrors));
        md.AppendLine();

        md.AppendLine("## Checks");
        md.AppendLine();
        md.AppendLine("Pass rate of each deterministic check over the turns it applies to.");
        md.AppendLine();
        md.AppendLine("| Check | Turns | Pass rate |");
        md.AppendLine("|---|---:|---:|");
        foreach (var (name, (applied, rate)) in all.Checks.Where(c => c.Value.Applied > 0))
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| `{name}` | {applied} | {rate * 100:0}% |");
        }

        md.AppendLine();
        Breakdown(md, "By locale", results.GroupBy(r => r.Case.Locale).OrderBy(g => g.Key, StringComparer.Ordinal));
        Breakdown(md, "By tag", results.SelectMany(r => r.Case.Tags.Select(t => (Tag: t, Result: r))).GroupBy(p => p.Tag, p => p.Result).OrderBy(g => g.Key, StringComparer.Ordinal));

        md.AppendLine("## Cases");
        md.AppendLine();
        md.AppendLine("| Case | Locale | Tags | Passed | Failed checks (turns) | Judge |");
        md.AppendLine("|---|---|---|---:|---|---:|");
        foreach (var result in results)
        {
            var failed = result.Runs.SelectMany(r => r.Turns).SelectMany(t => t.Checks).Where(c => !c.Passed)
                .GroupBy(c => c.Name).Select(g => $"`{g.Key}` ×{g.Count()}");
            var judged = result.Runs.Where(r => r.Judge is not null).ToList();
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| `{result.Case.Id}` | {result.Case.Locale} | {string.Join(", ", result.Case.Tags)} | {result.Runs.Count(r => r.Passed)}/{result.Runs.Count} | {string.Join(", ", failed)} | {(judged.Count == 0 ? "—" : judged.Average(r => r.Judge!.Mean).ToString("0.00", CultureInfo.InvariantCulture))} |");
        }

        md.AppendLine();
        Failures(md, results);
        JudgeComments(md, results);

        md.AppendLine("## Conclusion");
        md.AppendLine();
        md.AppendLine("_Written by hand after the run._");
        return md.ToString();

        void Row(string name, string value) => md.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {value} |");
    }

    public static object ToJson(RunHeader header, IReadOnlyList<AssistantCaseResult> results) => new
    {
        header,
        summary = Summary(AssistantSummary.Of(results)),
        cases = results.Select(r => new
        {
            r.Case.Id,
            r.Case.Locale,
            r.Case.Tags,
            runs = r.Runs.Select(run => new
            {
                run.Repeat,
                run.Passed,
                run.WallMs,
                run.Judge,
                run.JudgeError,
                turns = run.Turns.Select(t => new
                {
                    t.Number,
                    t.User,
                    t.Answer,
                    t.Passed,
                    t.Tools,
                    cards = t.Cards.Select(c => c.TmdbId),
                    cart = t.CartAfter.Select(l => new { l.TmdbId, l.Offer }),
                    t.ConfirmationRequired,
                    t.Error,
                    t.InputTokens,
                    t.OutputTokens,
                    t.LatencyMs,
                    t.ModelCalls,
                    t.UnknownTitles,
                    t.DetectedLanguage,
                    failed = t.Checks.Where(c => !c.Passed),
                }),
            }),
        }),
    };

    private static object Summary(AssistantSummary s) => new
    {
        s.Cases,
        s.Runs,
        s.Turns,
        s.PassRate,
        s.StableCases,
        s.FlakyCases,
        s.FailingCases,
        checks = s.Checks.Where(c => c.Value.Applied > 0).ToDictionary(c => c.Key, c => new { c.Value.Applied, c.Value.PassRate }),
        s.GroundingViolationRate,
        s.Titles,
        s.UnknownTitles,
        s.Judge,
        s.JudgeMean,
        s.JudgeFailures,
        s.InputTokensPerTurn,
        s.OutputTokensPerTurn,
        s.InputTokens,
        s.OutputTokens,
        s.ModelCallsPerTurn,
        s.IterationLimitHits,
        s.ProviderErrors,
        s.LatencyP50Ms,
        s.LatencyP95Ms,
    };

    private static void Breakdown(StringBuilder md, string title, IEnumerable<IGrouping<string, AssistantCaseResult>> groups)
    {
        md.AppendLine(CultureInfo.InvariantCulture, $"## {title}");
        md.AppendLine();
        md.AppendLine("| Group | Cases | Pass rate | Grounding violations | Judge mean |");
        md.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var group in groups)
        {
            var s = AssistantSummary.Of([.. group]);
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {group.Key} | {s.Cases} | {Percent(s.PassRate)} | {s.GroundingViolationRate * 100:0}% | {(s.Judge.Count == 0 ? "—" : s.JudgeMean.Mean.ToString("0.00", CultureInfo.InvariantCulture))} |");
        }

        md.AppendLine();
    }

    private static void Failures(StringBuilder md, IReadOnlyList<AssistantCaseResult> results)
    {
        md.AppendLine("## Failed turns");
        md.AppendLine();
        var any = false;
        foreach (var result in results)
        {
            foreach (var run in result.Runs)
            {
                foreach (var turn in run.Turns.Where(t => !t.Passed))
                {
                    any = true;
                    md.AppendLine(CultureInfo.InvariantCulture, $"### `{result.Case.Id}` #{run.Repeat}, turn {turn.Number}");
                    md.AppendLine();
                    foreach (var check in turn.Checks.Where(c => !c.Passed))
                    {
                        md.AppendLine(CultureInfo.InvariantCulture, $"- **{check.Name}**: {check.Detail}");
                    }

                    md.AppendLine(CultureInfo.InvariantCulture, $"- User: {Excerpt(turn.User)}");
                    md.AppendLine(CultureInfo.InvariantCulture, $"- Tools: {(turn.Tools.Count == 0 ? "none" : string.Join("; ", turn.Tools.Select(t => $"`{t.Name}` {Excerpt(t.Args, 160)}{(t.Ok ? "" : " (error)")}")))}");
                    if (turn.CartAfter.Count > 0)
                    {
                        md.AppendLine(CultureInfo.InvariantCulture, $"- Cart: {string.Join("; ", turn.CartAfter.Select(l => $"{l.Title} ({l.Offer})"))}");
                    }

                    md.AppendLine(CultureInfo.InvariantCulture, $"- Answer: {Excerpt(turn.Answer)}");
                    md.AppendLine();
                }
            }
        }

        if (!any)
        {
            md.AppendLine("None.");
            md.AppendLine();
        }
    }

    private static void JudgeComments(StringBuilder md, IReadOnlyList<AssistantCaseResult> results)
    {
        var low = results.SelectMany(r => r.Runs.Where(run => run.Judge is { Mean: < 3.5 }).Select(run => (r.Case, Run: run))).ToList();
        var failed = results.SelectMany(r => r.Runs.Where(run => run.JudgeError is not null).Select(run => (r.Case, Run: run))).ToList();
        if (low.Count == 0 && failed.Count == 0)
        {
            return;
        }

        md.AppendLine("## Judge: runs scored below 3.5");
        md.AppendLine();
        foreach (var (c, run) in low)
        {
            var j = run.Judge!;
            md.AppendLine(CultureInfo.InvariantCulture,
                $"- `{c.Id}` #{run.Repeat}: relevance {j.Relevance}, explanations {j.Explanations}, tone {j.Tone}, constraints {j.Constraints}. {Excerpt(j.Comment)}");
        }

        foreach (var (c, run) in failed)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"- `{c.Id}` #{run.Repeat}: no score ({run.JudgeError})");
        }

        md.AppendLine();
    }

    private static string Excerpt(string text, int length = ExcerptLength)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Replace("|", "\\|", StringComparison.Ordinal);
        return line.Length <= length ? line : string.Concat(line.AsSpan(0, length), "…");
    }

    private static string Percent(Spread spread) =>
        string.Create(CultureInfo.InvariantCulture, $"{spread.Mean * 100:0}% ± {spread.StdDev * 100:0}");

    private static string Score(Spread spread) =>
        string.Create(CultureInfo.InvariantCulture, $"{spread.Mean:0.00} ± {spread.StdDev:0.00}");

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
