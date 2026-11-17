using System.Diagnostics;
using FilmSearch.AI.Telemetry;
using FilmSearch.Core.Search;
using FilmSearch.Evals.Datasets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.Evals.Suites;

/// <summary>One call of query understanding for one case, compared field by field with the expected output.</summary>
public sealed record QuRun(
    UnderstoodQuery Understood,
    int Attempts,
    double LatencyMs,
    IReadOnlyDictionary<string, bool> Fields)
{
    public bool Valid => Understood.Source != QueryUnderstandingSource.Fallback;

    public bool ExactMatch => Valid && Fields.Values.All(ok => ok);
}

public sealed record QuCaseResult(QuCase Case, IReadOnlyList<QuRun> Runs);

/// <summary>
/// Query understanding suite (spec 09 §3): each case of <c>qu.golden.jsonl</c> through <see cref="IQueryUnderstanding"/>,
/// with the cache cleared before every call, so every repeat asks the model.
/// </summary>
public sealed class QuSuite(IServiceProvider services)
{
    public const string GenresInclude = "genresInclude";
    public const string GenresExclude = "genresExclude";
    public const string YearFrom = "yearFrom";
    public const string YearTo = "yearTo";
    public const string MinRating = "minRating";
    public const string MaxPrice = "maxPriceMinor";
    public const string Language = "language";
    public const string SimilarTo = "similarToTitles";

    public static IReadOnlyList<string> FieldNames { get; } = [GenresInclude, GenresExclude, YearFrom, YearTo, MinRating, MaxPrice, Language, SimilarTo];

    /// <summary>Tag of the <c>query_understanding</c> span with the number of model calls (1, or 2 after a retry).</summary>
    private const string AttemptsTag = "filmsearch.qu.attempts";

    public async Task<IReadOnlyList<QuCaseResult>> RunAsync(IReadOnlyList<QuCase> cases, int repeat, CancellationToken cancellationToken)
    {
        var attempts = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AiMetrics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "query_understanding" && activity.GetTagItem(AttemptsTag) is int value)
                {
                    attempts = value;
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        Console.Error.WriteLine($"Query understanding: {cases.Count} cases × {repeat}");
        var results = new List<QuCaseResult>();
        foreach (var quCase in cases)
        {
            var runs = new List<QuRun>();
            for (var i = 0; i < repeat; i++)
            {
                if (services.GetRequiredService<IMemoryCache>() is MemoryCache cache)
                {
                    cache.Clear();
                }

                attempts = 0;
                await using var scope = services.CreateAsyncScope();
                var understanding = scope.ServiceProvider.GetRequiredService<IQueryUnderstanding>();
                var stopwatch = Stopwatch.StartNew();
                var understood = await understanding.UnderstandAsync(quCase.Query, cancellationToken);
                stopwatch.Stop();

                runs.Add(new QuRun(understood, attempts, stopwatch.Elapsed.TotalMilliseconds, Compare(quCase.Expected, understood)));
            }

            results.Add(new QuCaseResult(quCase, runs));
            if (results.Count % 10 == 0)
            {
                Console.Error.WriteLine($"  {results.Count}/{cases.Count}");
            }
        }

        return results;
    }

    /// <summary>Field → correct. Lists compare as sets; "not mentioned" must stay empty or null.</summary>
    public static IReadOnlyDictionary<string, bool> Compare(QuExpected expected, UnderstoodQuery actual)
    {
        var f = actual.Filters;
        return new Dictionary<string, bool>
        {
            [GenresInclude] = f.GenresInclude.ToHashSet().SetEquals(expected.GenresInclude),
            [GenresExclude] = f.GenresExclude.ToHashSet().SetEquals(expected.GenresExclude),
            [YearFrom] = f.YearFrom == expected.YearFrom,
            [YearTo] = f.YearTo == expected.YearTo,
            [MinRating] = f.MinRating == expected.MinRating,
            [MaxPrice] = f.MaxPriceMinor == expected.MaxPriceMinor,
            [Language] = string.Equals(f.Language, expected.Language, StringComparison.OrdinalIgnoreCase),
            [SimilarTo] = TitlesMatch(expected.SimilarToTitles, actual.SimilarToTitles),
        };
    }

    /// <summary>
    /// Same number of titles, and each expected title is contained in a predicted one or the other way round, after
    /// <see cref="NormalizeTitle"/>: "Матрицу" (as written in "похожие на Матрицу") matches "Матрица".
    /// </summary>
    public static bool TitlesMatch(IReadOnlyList<string> expected, IReadOnlyList<string> actual) =>
        expected.Count == actual.Count
        && expected.Select(NormalizeTitle).All(e => actual.Select(NormalizeTitle).Any(a =>
            a.Contains(e, StringComparison.Ordinal) || e.Contains(a, StringComparison.Ordinal)));

    /// <summary>Lower case, ё as е, and the last letter of words longer than four letters dropped (a crude stem for Russian case endings).</summary>
    internal static string NormalizeTitle(string title) =>
        string.Join(' ', title.ToLowerInvariant().Replace('ё', 'е')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 4 ? w[..^1] : w));
}
