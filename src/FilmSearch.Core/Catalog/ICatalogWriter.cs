namespace FilmSearch.Core.Catalog;

/// <summary>
/// Write side of the Catalog module, used by ingestion (seed file now, TMDB sync in I16).
/// Upserts are idempotent: keyed by TMDB id, and a movie whose imported data did not change is not touched.
/// Safe to call from several processes at once (writes are serialized in the database).
/// </summary>
public interface ICatalogWriter
{
    Task UpsertGenresAsync(IReadOnlyList<GenreImport> genres, CancellationToken cancellationToken);

    /// <summary>Upserts one batch in one transaction. Genres must exist; unknown genre ids are ignored.</summary>
    Task<UpsertResult> UpsertMoviesAsync(IReadOnlyList<MovieImport> movies, MovieSource source, CancellationToken cancellationToken);
}

public sealed record UpsertResult(int Created, int Updated, int Unchanged)
{
    public static UpsertResult Empty { get; } = new(0, 0, 0);

    public int Total => Created + Updated + Unchanged;

    public UpsertResult Add(UpsertResult other) =>
        new(Created + other.Created, Updated + other.Updated, Unchanged + other.Unchanged);
}
