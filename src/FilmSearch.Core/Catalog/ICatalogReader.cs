namespace FilmSearch.Core.Catalog;

/// <summary>Read side of the Catalog module, used by the HTTP endpoints and by other modules (spec 02 §5).</summary>
public interface ICatalogReader
{
    Task<PagedResult<MovieSummary>> ListMoviesAsync(MovieListQuery query, string locale, CancellationToken cancellationToken);

    /// <summary>Movies by id in the order of <paramref name="ids"/>; unknown ids are skipped.</summary>
    Task<IReadOnlyList<MovieSummary>> GetMoviesAsync(IReadOnlyList<Guid> ids, string locale, CancellationToken cancellationToken);

    Task<MovieDetails?> GetMovieAsync(Guid id, string locale, CancellationToken cancellationToken);

    Task<IReadOnlyList<GenreItem>> ListGenresAsync(string locale, CancellationToken cancellationToken);

    Task<bool> HasMoviesAsync(CancellationToken cancellationToken);
}
