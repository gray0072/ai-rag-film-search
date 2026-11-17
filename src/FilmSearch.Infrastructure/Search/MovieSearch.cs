using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Search.Hybrid;
using FilmSearch.Infrastructure.Search.Keyword;
using FilmSearch.Infrastructure.Search.Semantic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FilmSearch.Infrastructure.Search;

/// <summary>
/// <see cref="IMovieSearch"/>: optional query understanding, then the retriever(s) of the requested mode, then the movies
/// through <see cref="ICatalogReader"/> in the requested locale, keeping the ranking order.
/// Keyword mode from I04, semantic mode and similar movies from I06, hybrid mode and query understanding from I07.
/// </summary>
internal sealed partial class MovieSearch(
    KeywordRetriever keyword,
    SemanticRetriever semantic,
    ICatalogReader catalog,
    IQueryUnderstanding understanding,
    IOptions<HybridSearchOptions> hybridOptions,
    ILogger<MovieSearch> logger) : IMovieSearch
{
    public bool Supports(SearchMode mode) => Enum.IsDefined(mode);

    public async Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Text);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);

        if (!Supports(query.Mode))
        {
            throw new NotSupportedException($"Search mode {query.Mode} is not implemented.");
        }

        var plan = await PlanAsync(query, cancellationToken);
        var skip = (query.Page - 1) * query.PageSize;
        var result = query.Mode switch
        {
            SearchMode.Semantic => await SemanticAsync(query, plan, skip, cancellationToken),
            SearchMode.Hybrid => await HybridAsync(query, plan, skip, cancellationToken),
            _ => await KeywordAsync(query, plan, skip, cancellationToken),
        };

        return result with { AppliedFilter = plan.Filter, Understood = plan.Understood };
    }

    public async Task<IReadOnlyList<SearchItem>?> FindSimilarAsync(
        Guid movieId,
        MovieFilter filter,
        string locale,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        if ((await catalog.GetMoviesAsync([movieId], locale, cancellationToken)).Count == 0)
        {
            return null;
        }

        var hits = await semantic.SimilarAsync(movieId, filter, limit, cancellationToken);
        return await ToItemsAsync(hits, h => h.MovieId, h => h.Similarity, VectorDebug, locale, debug: false, cancellationToken);
    }

    /// <summary>
    /// What the retrievers get. Without query understanding: the query text and filters as they are. With it: the semantic
    /// text of the model for both retrievers (filter words removed), the merged filters, and the movies of
    /// "movies like X", which are left out of the results and steer the vector search.
    /// </summary>
    private async Task<SearchPlan> PlanAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        if (!query.Understand)
        {
            return new SearchPlan(query.Text, query.Filter, [], null);
        }

        var understood = await understanding.UnderstandAsync(query.Text, cancellationToken);
        var similarIds = await ResolveTitlesAsync(understood.SimilarToTitles, cancellationToken);
        var filter = understood.Filters.MergeInto(query.Filter) with
        {
            ExcludedMovieIds = [.. query.Filter.ExcludedMovieIds.Union(similarIds)],
        };
        var text = string.IsNullOrWhiteSpace(understood.SemanticQuery) ? query.Text : understood.SemanticQuery;

        return new SearchPlan(text, filter, similarIds, understood);
    }

    /// <summary>A title resolves to the best keyword hit when all its words match (title words have weight A).</summary>
    private async Task<IReadOnlyList<Guid>> ResolveTitlesAsync(IReadOnlyList<string> titles, CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        foreach (var title in titles.Take(hybridOptions.Value.MaxSimilarTitles))
        {
            var hits = await keyword.SearchAsync(title, MovieFilter.None, 0, 1, cancellationToken);
            if (hits.Match == KeywordRetriever.MatchAll && hits.Hits.Count > 0 && !ids.Contains(hits.Hits[0].MovieId))
            {
                ids.Add(hits.Hits[0].MovieId);
            }
        }

        return ids;
    }

    private async Task<SearchResult> KeywordAsync(SearchQuery query, SearchPlan plan, int skip, CancellationToken cancellationToken)
    {
        var result = await keyword.SearchAsync(plan.Text, plan.Filter, skip, query.PageSize, cancellationToken);
        var items = await ToItemsAsync(
            result.Hits,
            h => h.MovieId,
            h => h.Score,
            h => new SearchDebug { KeywordRank = h.Rank, KeywordScore = h.Score, KeywordMatch = result.Match },
            query.Locale,
            query.Debug,
            cancellationToken);

        return new SearchResult(items, query.Page, query.PageSize, result.Total);
    }

    private async Task<SearchResult> SemanticAsync(SearchQuery query, SearchPlan plan, int skip, CancellationToken cancellationToken)
    {
        // The whole list is at most Search:Semantic:MaxResults long: it is fetched at once and paged in memory.
        // "Movies like X" always blends vectors here: one vector list keeps the score a real similarity.
        var vector = await semantic.EmbedQueryAsync(plan.Text, cancellationToken);
        vector = await BlendAsync(vector, plan.SimilarMovieIds, cancellationToken);
        var hits = await semantic.NearestAsync(vector, plan.Filter, semantic.MaxResults, cancellationToken);
        var page = hits.Skip(skip).Take(query.PageSize).ToList();
        var items = await ToItemsAsync(page, h => h.MovieId, h => h.Similarity, VectorDebug, query.Locale, query.Debug, cancellationToken);

        return new SearchResult(items, query.Page, query.PageSize, hits.Count);
    }

    /// <summary>
    /// Hybrid search (spec 05 §4): the top candidates of keyword and semantic search fused with RRF. Keyword search is
    /// good at exact titles and names, semantic search at descriptions and other languages; fusion keeps what both do well.
    /// </summary>
    private async Task<SearchResult> HybridAsync(SearchQuery query, SearchPlan plan, int skip, CancellationToken cancellationToken)
    {
        var options = hybridOptions.Value;

        // The slow part is the embedding call (an HTTP call to the model); it runs while the keyword query runs.
        // The two database queries stay sequential: one DbContext does not allow parallel queries.
        var embedding = TryEmbedAsync(plan.Text, cancellationToken);
        var keywordHits = await keyword.SearchAsync(plan.Text, plan.Filter, 0, options.CandidatesPerList, cancellationToken);
        var vector = await embedding;

        IReadOnlyList<VectorHit> vectorHits = [];
        var keywordWeight = keywordHits.Match == KeywordRetriever.MatchAll ? options.KeywordWeight : options.RelaxedKeywordWeight;
        var lists = new List<RankedList> { new([.. keywordHits.Hits.Select(h => h.MovieId)], keywordWeight) };
        if (vector is { } queryVector)
        {
            if (options.SimilarTitles == SimilarTitlesStrategy.AverageVector)
            {
                queryVector = await BlendAsync(queryVector, plan.SimilarMovieIds, cancellationToken);
            }

            vectorHits = await semantic.NearestAsync(queryVector, plan.Filter, options.CandidatesPerList, cancellationToken);
            lists.Add(new([.. vectorHits.Select(h => h.MovieId)], options.SemanticWeight));
        }

        if (options.SimilarTitles == SimilarTitlesStrategy.ExtraList && plan.SimilarMovieIds.Count > 0)
        {
            var titleVectors = await semantic.GetMovieVectorsAsync(plan.SimilarMovieIds, cancellationToken);
            foreach (var titleVector in titleVectors.Values)
            {
                var neighbours = await semantic.NearestAsync(titleVector, plan.Filter, options.CandidatesPerList, cancellationToken);
                lists.Add(new([.. neighbours.Select(h => h.MovieId)], options.SimilarTitleWeight));
            }
        }

        var fused = ReciprocalRankFusion.Fuse(lists, options.RrfK);
        var keywordById = keywordHits.Hits.ToDictionary(h => h.MovieId);
        var vectorById = vectorHits.ToDictionary(h => h.MovieId);
        var page = fused.Skip(skip).Take(query.PageSize).ToList();
        var items = await ToItemsAsync(
            page,
            h => h.MovieId,
            h => Math.Round(h.Score, 6),
            h =>
            {
                var keywordHit = keywordById.GetValueOrDefault(h.MovieId);
                var vectorHit = vectorById.GetValueOrDefault(h.MovieId);
                return new SearchDebug
                {
                    KeywordRank = keywordHit?.Rank,
                    KeywordScore = keywordHit?.Score,
                    KeywordMatch = keywordHit is null ? null : keywordHits.Match,
                    VectorRank = vectorHit?.Rank,
                    Similarity = vectorHit?.Similarity,
                    FusedScore = Math.Round(h.Score, 6),
                };
            },
            query.Locale,
            query.Debug,
            cancellationToken);

        return new SearchResult(items, query.Page, query.PageSize, fused.Count) { Degraded = vector is null };
    }

    /// <summary>Null when the embedding provider is down: hybrid search then returns the keyword results (degraded).</summary>
    private async Task<ReadOnlyMemory<float>?> TryEmbedAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            return await semantic.EmbedQueryAsync(text, cancellationToken);
        }
        catch (AiProviderUnavailableException ex)
        {
            LogEmbeddingFailed(logger, ex);
            return null;
        }
    }

    private async Task<ReadOnlyMemory<float>> BlendAsync(ReadOnlyMemory<float> query, IReadOnlyList<Guid> similarIds, CancellationToken cancellationToken)
    {
        if (similarIds.Count == 0)
        {
            return query;
        }

        var vectors = await semantic.GetMovieVectorsAsync(similarIds, cancellationToken);
        return QueryVectors.Blend(query, [.. vectors.Values]);
    }

    private static SearchDebug VectorDebug(VectorHit hit) => new() { VectorRank = hit.Rank, Similarity = hit.Similarity };

    private async Task<IReadOnlyList<SearchItem>> ToItemsAsync<THit>(
        IReadOnlyList<THit> hits,
        Func<THit, Guid> movieId,
        Func<THit, double> score,
        Func<THit, SearchDebug> debugOf,
        string locale,
        bool debug,
        CancellationToken cancellationToken)
    {
        var movies = await catalog.GetMoviesAsync([.. hits.Select(movieId)], locale, cancellationToken);
        var byId = movies.ToDictionary(m => m.Id);

        return [.. hits
            .Where(h => byId.ContainsKey(movieId(h)))
            .Select(h => new SearchItem(byId[movieId(h)], score(h), debug ? debugOf(h) : null))];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hybrid search falls back to keyword results: the query could not be embedded")]
    private static partial void LogEmbeddingFailed(ILogger logger, Exception exception);

    private sealed record SearchPlan(string Text, MovieFilter Filter, IReadOnlyList<Guid> SimilarMovieIds, UnderstoodQuery? Understood);
}
