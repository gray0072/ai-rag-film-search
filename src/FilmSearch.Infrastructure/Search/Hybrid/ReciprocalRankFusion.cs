namespace FilmSearch.Infrastructure.Search.Hybrid;

/// <summary>One ranked list to fuse: movie ids best first and the weight of the list.</summary>
internal sealed record RankedList(IReadOnlyList<Guid> MovieIds, double Weight = 1.0);

/// <summary>A fused movie: its RRF score and its rank (1-based) in each input list, null where it is absent.</summary>
internal sealed record FusedHit(Guid MovieId, double Score, IReadOnlyList<int?> Ranks);

/// <summary>
/// Reciprocal Rank Fusion (spec 05 §4): <c>score(d) = Σ weight_i / (k + rank_i(d))</c> over the lists that contain d.
/// <para>
/// RRF uses only ranks, not scores, so it can fuse lists whose scores are not comparable (<c>ts_rank_cd</c> vs cosine
/// similarity) without any normalization. <c>k</c> (60 in the original paper) flattens the curve: with k = 60 rank 1
/// gives 1/61 ≈ 0.0164 and rank 10 gives 1/70 ≈ 0.0143, so a movie found by both retrievers at moderate ranks beats a
/// movie found by one retriever at rank 1. A smaller k trusts the top of each list more.
/// </para>
/// </summary>
internal static class ReciprocalRankFusion
{
    /// <summary>k of the original RRF paper (Cormack et al., 2009). The search uses <see cref="HybridSearchOptions.RrfK"/>.</summary>
    public const int DefaultK = 60;

    /// <summary>Fused hits, best first. Ties: better best rank, then more lists, then movie id (deterministic).</summary>
    public static IReadOnlyList<FusedHit> Fuse(IReadOnlyList<RankedList> lists, int k = DefaultK)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k);

        var ranks = new Dictionary<Guid, int?[]>();
        for (var list = 0; list < lists.Count; list++)
        {
            var ids = lists[list].MovieIds;
            for (var position = 0; position < ids.Count; position++)
            {
                if (!ranks.TryGetValue(ids[position], out var movieRanks))
                {
                    movieRanks = new int?[lists.Count];
                    ranks[ids[position]] = movieRanks;
                }

                // A list should not repeat a movie; if it does, its best position counts.
                movieRanks[list] ??= position + 1;
            }
        }

        return [.. ranks
            .Select(pair => new FusedHit(pair.Key, Score(pair.Value, lists, k), pair.Value))
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Ranks.Min() ?? int.MaxValue)
            .ThenByDescending(hit => hit.Ranks.Count(r => r is not null))
            .ThenBy(hit => hit.MovieId)];
    }

    private static double Score(int?[] ranks, IReadOnlyList<RankedList> lists, int k)
    {
        var score = 0.0;
        for (var list = 0; list < ranks.Length; list++)
        {
            if (ranks[list] is { } rank)
            {
                score += lists[list].Weight / (k + rank);
            }
        }

        return score;
    }
}
