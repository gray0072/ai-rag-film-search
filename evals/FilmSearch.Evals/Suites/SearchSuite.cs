using System.Diagnostics;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using FilmSearch.Evals.Datasets;
using FilmSearch.Evals.Metrics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.Evals.Suites;

/// <summary>
/// One search of one case in one mode: <c>Results</c> are the TMDB ids of the top <see cref="SearchSuite.K"/>, best
/// first; <c>Violations</c> counts results in it with a genre the case excludes.
/// </summary>
public sealed record SearchRun(
    IReadOnlyList<int> Results,
    double Recall5,
    double Recall10,
    double Mrr,
    double Ndcg10,
    int Violations,
    double LatencyMs,
    bool Degraded,
    UnderstoodQuery? Understood);

/// <summary>Results of one case in one mode: metrics averaged over the repeats, and every run.</summary>
public sealed record SearchCaseResult(SearchCase Case, string Mode, IReadOnlyList<SearchRun> Runs)
{
    public double Recall5 => Runs.Average(r => r.Recall5);

    public double Recall10 => Runs.Average(r => r.Recall10);

    public double Mrr => Runs.Average(r => r.Mrr);

    public double Ndcg10 => Runs.Average(r => r.Ndcg10);

    /// <summary>Share of the top <see cref="SearchSuite.K"/> with an excluded genre; null when the case excludes none.</summary>
    public double? ViolationRate => Case.ExcludedGenres is { Count: > 0 }
        ? Runs.Average(r => (double)r.Violations / SearchSuite.K)
        : null;
}

/// <summary>
/// Search suite (spec 09 §3): every case of <c>search.golden.jsonl</c> through <see cref="IMovieSearch"/> in each mode,
/// without UI filters, in the case's locale. Recall@5, Recall@10, MRR@10 and nDCG@10 per case; latency per call.
/// </summary>
public sealed class SearchSuite(IServiceProvider services, IReadOnlyDictionary<Guid, int> tmdbIds)
{
    /// <summary>Depth of the evaluated result list.</summary>
    public const int K = 10;

    public async Task<IReadOnlyList<SearchCaseResult>> RunAsync(
        IReadOnlyList<SearchCase> cases,
        IReadOnlyList<SearchModeOption> modes,
        int repeat,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchCaseResult>();
        foreach (var mode in modes)
        {
            Console.Error.WriteLine($"Mode {mode.Name}: {cases.Count} cases × {repeat}");
            await WarmUpAsync(mode, cancellationToken);
            for (var c = 0; c < cases.Count; c++)
            {
                var runs = new List<SearchRun>();
                for (var i = 0; i < repeat; i++)
                {
                    runs.Add(await RunOnceAsync(cases[c], mode, cancellationToken));
                }

                results.Add(new SearchCaseResult(cases[c], mode.Name, runs));
                if ((c + 1) % 25 == 0)
                {
                    Console.Error.WriteLine($"  {c + 1}/{cases.Count}");
                }
            }
        }

        return results;
    }

    /// <summary>The first call of a mode pays for connections, JIT and model loading; it is not measured.</summary>
    private async Task WarmUpAsync(SearchModeOption mode, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var search = scope.ServiceProvider.GetRequiredService<IMovieSearch>();
        await search.SearchAsync(new SearchQuery("warm up", mode.Mode, MovieFilter.None, "en", PageSize: K, Understand: mode.Understand), cancellationToken);
    }

    private async Task<SearchRun> RunOnceAsync(SearchCase searchCase, SearchModeOption mode, CancellationToken cancellationToken)
    {
        if (mode.Understand)
        {
            // Every run asks the model: a cached answer would hide its latency and its variance between repeats.
            ClearCache();
        }

        await using var scope = services.CreateAsyncScope();
        var search = scope.ServiceProvider.GetRequiredService<IMovieSearch>();
        var query = new SearchQuery(searchCase.Query, mode.Mode, MovieFilter.None, searchCase.Locale, PageSize: K, Understand: mode.Understand);

        var stopwatch = Stopwatch.StartNew();
        var result = await search.SearchAsync(query, cancellationToken);
        stopwatch.Stop();

        var ranked = result.Items.Select(i => tmdbIds.GetValueOrDefault(i.Movie.Id)).ToList();
        var excluded = searchCase.ExcludedGenres ?? [];
        var violations = result.Items.Count(i => i.Movie.Genres.Any(g => excluded.Contains(g.Slug)));
        var grades = searchCase.Grades();

        return new SearchRun(
            ranked,
            RetrievalMetrics.RecallAt(ranked, grades, 5),
            RetrievalMetrics.RecallAt(ranked, grades, K),
            RetrievalMetrics.ReciprocalRankAt(ranked, grades, K),
            RetrievalMetrics.NdcgAt(ranked, grades, K),
            violations,
            stopwatch.Elapsed.TotalMilliseconds,
            result.Degraded,
            result.Understood);
    }

    private void ClearCache()
    {
        if (services.GetRequiredService<IMemoryCache>() is MemoryCache cache)
        {
            cache.Clear();
        }
    }
}
