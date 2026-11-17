using FilmSearch.Core.Catalog;

namespace FilmSearch.Core.Search;

/// <summary>
/// Movie search used by the UI and the assistant (spec 05). Filters are applied inside each retrieval query
/// (pre-filtering), so strict filters never empty a top-k list taken before filtering.
/// </summary>
public interface IMovieSearch
{
    /// <summary>True when <paramref name="mode"/> is implemented (keyword from I04, semantic from I06, hybrid from I07).</summary>
    bool Supports(SearchMode mode);

    /// <exception cref="NotSupportedException">The mode is not implemented yet (see <see cref="Supports"/>).</exception>
    Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Movies nearest to the given movie in the embedding space of the active model (vector neighbours, from I06),
    /// best first, without the movie itself. Empty when the movie has no vector yet; null when the movie does not exist.
    /// </summary>
    Task<IReadOnlyList<SearchItem>?> FindSimilarAsync(Guid movieId, MovieFilter filter, string locale, int limit, CancellationToken cancellationToken);
}
