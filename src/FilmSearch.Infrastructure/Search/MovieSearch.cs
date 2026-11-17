using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Search.Keyword;

namespace FilmSearch.Infrastructure.Search;

/// <summary>
/// <see cref="IMovieSearch"/>: runs the retriever of the requested mode, then loads the movies through
/// <see cref="ICatalogReader"/> in the requested locale, keeping the ranking order.
/// Keyword mode from I04; semantic (I06) and hybrid (I07) add their retrievers here.
/// </summary>
internal sealed class MovieSearch(KeywordRetriever keyword, ICatalogReader catalog) : IMovieSearch
{
    public bool Supports(SearchMode mode) => mode == SearchMode.Keyword;

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
        var result = await keyword.SearchAsync(query.Text, query.Filter, skip, query.PageSize, cancellationToken);

        var movies = await catalog.GetMoviesAsync([.. result.Hits.Select(h => h.MovieId)], query.Locale, cancellationToken);
        var byId = movies.ToDictionary(m => m.Id);

        var items = result.Hits
            .Where(h => byId.ContainsKey(h.MovieId))
            .Select(h => new SearchItem(
                byId[h.MovieId],
                h.Score,
                query.Debug
                    ? new SearchDebug { KeywordRank = h.Rank, KeywordScore = h.Score, KeywordMatch = result.Match }
                    : null))
            .ToList();

        return new SearchResult(items, query.Page, query.PageSize, result.Total);
    }
}
