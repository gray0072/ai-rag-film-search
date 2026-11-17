using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Search.Keyword;
using FilmSearch.Infrastructure.Search.Semantic;

namespace FilmSearch.Infrastructure.Search;

/// <summary>
/// <see cref="IMovieSearch"/>: runs the retriever of the requested mode, then loads the movies through
/// <see cref="ICatalogReader"/> in the requested locale, keeping the ranking order.
/// Keyword mode from I04, semantic mode and similar movies from I06; hybrid (I07) adds fusion here.
/// </summary>
internal sealed class MovieSearch(KeywordRetriever keyword, SemanticRetriever semantic, ICatalogReader catalog) : IMovieSearch
{
    public bool Supports(SearchMode mode) => mode is SearchMode.Keyword or SearchMode.Semantic;

    public async Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Text);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);

        if (!Supports(query.Mode))
        {
            throw new NotSupportedException($"Search mode {query.Mode} is not implemented yet.");
        }

        var skip = (query.Page - 1) * query.PageSize;
        return query.Mode == SearchMode.Semantic
            ? await SemanticAsync(query, skip, cancellationToken)
            : await KeywordAsync(query, skip, cancellationToken);
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

    private async Task<SearchResult> KeywordAsync(SearchQuery query, int skip, CancellationToken cancellationToken)
    {
        var result = await keyword.SearchAsync(query.Text, query.Filter, skip, query.PageSize, cancellationToken);
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

    private async Task<SearchResult> SemanticAsync(SearchQuery query, int skip, CancellationToken cancellationToken)
    {
        // The whole list is at most Search:Semantic:MaxResults long: it is fetched at once and paged in memory.
        var hits = await semantic.SearchAsync(query.Text, query.Filter, cancellationToken);
        var page = hits.Skip(skip).Take(query.PageSize).ToList();
        var items = await ToItemsAsync(page, h => h.MovieId, h => h.Similarity, VectorDebug, query.Locale, query.Debug, cancellationToken);

        return new SearchResult(items, query.Page, query.PageSize, hits.Count);
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
}
