using FilmSearch.Core.Catalog;
using FilmSearch.Infrastructure.Catalog;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FilmSearch.Infrastructure.Search.Keyword;

/// <summary>A movie found by keyword search. <see cref="Rank"/> starts at 1.</summary>
internal sealed record KeywordHit(Guid MovieId, int Rank, double Score);

/// <summary>One page of keyword hits, the total number of matches and how the query matched (<see cref="KeywordRetriever.MatchAll"/> or <see cref="KeywordRetriever.MatchAny"/>).</summary>
internal sealed record KeywordHits(IReadOnlyList<KeywordHit> Hits, int Total, string Match);

/// <summary>
/// Keyword retrieval (spec 05 §2), the "sparse" half of hybrid search: PostgreSQL full-text search over
/// <see cref="MovieSearchDocument"/> with the English, Russian and Swedish configs at once. The query is parsed by
/// <c>websearch_to_tsquery</c> per config; a movie matches when any config matches, and its score is the best
/// <c>ts_rank_cd</c> of the three. Catalog filters are applied in the same SQL query (pre-filtering).
/// When no movie contains all the words, the query is relaxed to any word, so a longer query still returns
/// the closest movies.
/// </summary>
internal sealed class KeywordRetriever(AppDbContext db)
{
    /// <summary>Every query term matched.</summary>
    public const string MatchAll = "all";

    /// <summary>No movie matched all terms; the query was relaxed to match any term.</summary>
    public const string MatchAny = "any";

    // rank / (rank + 1): scores in 0–1 with the same order as the raw rank.
    private const NpgsqlTsRankingNormalization Normalization = NpgsqlTsRankingNormalization.DivideByItselfPlusOne;

    public async Task<KeywordHits> SearchAsync(string text, MovieFilter filter, int skip, int take, CancellationToken cancellationToken)
    {
        var normalized = KeywordQuery.Normalize(text);
        var hits = await SearchAsync(normalized, filter, skip, take, MatchAll, cancellationToken);
        if (hits.Total == 0 && KeywordQuery.AnyWord(normalized) is { } anyWord)
        {
            hits = await SearchAsync(anyWord, filter, skip, take, MatchAny, cancellationToken);
        }

        return hits;
    }

    private async Task<KeywordHits> SearchAsync(
        string query,
        MovieFilter filter,
        int skip,
        int take,
        string match,
        CancellationToken cancellationToken)
    {
        var matches =
            from m in db.Set<Movie>().AsNoTracking().ApplyFilter(filter)
            join d in db.Set<MovieSearchDocument>().AsNoTracking() on m.Id equals d.MovieId
            let en = EF.Functions.WebSearchToTsQuery("english", query)
            let ru = EF.Functions.WebSearchToTsQuery("russian", query)
            let sv = EF.Functions.WebSearchToTsQuery("swedish", query)
            where d.SearchEn.Matches(en) || d.SearchRu.Matches(ru) || d.SearchSv.Matches(sv)
            select new
            {
                m.Id,
                m.Popularity,
                Score = Math.Max(
                    Math.Max(d.SearchEn.RankCoverDensity(en, Normalization), d.SearchRu.RankCoverDensity(ru, Normalization)),
                    d.SearchSv.RankCoverDensity(sv, Normalization)),
            };

        var total = await matches.CountAsync(cancellationToken);
        if (total == 0 || skip >= total)
        {
            return new KeywordHits([], total, match);
        }

        var page = await matches
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Popularity)
            .ThenBy(x => x.Id)
            .Skip(skip)
            .Take(take)
            .Select(x => new { x.Id, x.Score })
            .ToListAsync(cancellationToken);

        return new KeywordHits(
            [.. page.Select((x, i) => new KeywordHit(x.Id, skip + i + 1, Math.Round(x.Score, 6)))],
            total,
            match);
    }
}
