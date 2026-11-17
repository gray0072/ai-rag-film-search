namespace FilmSearch.Evals.Metrics;

/// <summary>
/// Ranking metrics of the search suite (spec 09 §3). <c>ranked</c> is the result list, best first; <c>grades</c> maps
/// each relevant id to its grade (1–3, higher is more relevant); ids not in it are not relevant.
/// </summary>
public static class RetrievalMetrics
{
    /// <summary>
    /// Share of the relevant items found in the top <paramref name="k"/>, out of the most that can be found there:
    /// <c>|relevant ∩ top k| / min(|relevant|, k)</c>. With 10 relevant movies and k = 5, five hits give 1.0.
    /// </summary>
    public static double RecallAt<T>(IReadOnlyList<T> ranked, IReadOnlyDictionary<T, int> grades, int k)
        where T : notnull
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);
        if (grades.Count == 0)
        {
            return 0;
        }

        var hits = ranked.Take(k).Distinct().Count(grades.ContainsKey);
        return (double)hits / Math.Min(grades.Count, k);
    }

    /// <summary>Reciprocal rank of the first relevant item in the top <paramref name="k"/>; 0 when there is none.</summary>
    public static double ReciprocalRankAt<T>(IReadOnlyList<T> ranked, IReadOnlyDictionary<T, int> grades, int k)
        where T : notnull
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);
        for (var i = 0; i < Math.Min(k, ranked.Count); i++)
        {
            if (grades.ContainsKey(ranked[i]))
            {
                return 1.0 / (i + 1);
            }
        }

        return 0;
    }

    /// <summary>
    /// Normalized discounted cumulative gain with graded relevance: <c>DCG = Σ (2^grade − 1) / log2(rank + 1)</c> over
    /// the top <paramref name="k"/>, divided by the DCG of the ideal order. A repeated id counts once.
    /// </summary>
    public static double NdcgAt<T>(IReadOnlyList<T> ranked, IReadOnlyDictionary<T, int> grades, int k)
        where T : notnull
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);
        var ideal = Dcg(grades.Values.Where(g => g > 0).OrderDescending().Take(k));
        if (ideal == 0)
        {
            return 0;
        }

        var seen = new HashSet<T>();
        var gains = ranked.Take(k).Select(id => seen.Add(id) ? grades.GetValueOrDefault(id) : 0);
        return Dcg(gains) / ideal;
    }

    /// <summary>Value at the given percentile (0–100) with linear interpolation between the closest ranks.</summary>
    public static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percentile, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentile, 100);
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToArray();
        var position = percentile / 100 * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }

    private static double Dcg(IEnumerable<int> gains) =>
        gains.Select((grade, i) => (Math.Pow(2, grade) - 1) / Math.Log2(i + 2)).Sum();
}
