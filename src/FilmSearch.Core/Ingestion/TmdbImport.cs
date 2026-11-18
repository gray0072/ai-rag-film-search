using FilmSearch.Core.Search;

namespace FilmSearch.Core.Ingestion;

// TMDB import (I16, FR-DATA-2, spec 08 §3): the parameters, progress and result of a TmdbImport job.
// Doc comments of types in the OpenAPI document stay on one line.

/// <summary>A TMDB movie list: <c>/movie/top_rated</c> or <c>/movie/popular</c>.</summary>
public enum TmdbMovieList
{
    TopRated,
    Popular,
}

/// <summary>What to import: the first <c>Pages</c> pages (20 movies each) of a list, or explicit TMDB ids.</summary>
public sealed record TmdbImportRequest(TmdbMovieList? List = null, int Pages = 1, IReadOnlyList<int>? TmdbIds = null);

public enum TmdbImportPhase
{
    Genres,
    Listing,
    Movies,
    Embeddings,
}

/// <summary>Progress of a TMDB import: <c>Fetched</c> of <c>Movies</c> details requested so far, upsert counters, and the embedding progress in the last phase.</summary>
public sealed record TmdbImportProgress(
    TmdbImportPhase Phase,
    int Movies,
    int Fetched,
    int Created,
    int Updated,
    int Unchanged,
    int Skipped,
    int NotFound,
    int Failed,
    ReindexProgress? Embeddings = null);

/// <summary>Outcome of a TMDB import. <c>Skipped</c>: adult or untitled movies; <c>NotFound</c>: ids TMDB does not know; <c>Failed</c>: movies whose details failed after retries (first errors in <c>Errors</c>); <c>Embeddings</c>: the stale-only reindex after the import.</summary>
public sealed record TmdbImportResult(
    int Movies,
    int Created,
    int Updated,
    int Unchanged,
    int Skipped,
    int NotFound,
    int Failed,
    IReadOnlyList<string> Errors,
    int Requests,
    ReindexResult? Embeddings,
    long ElapsedMilliseconds);

/// <summary>Imports movies from TMDB into the catalog and embeds the new and changed ones.</summary>
public interface ITmdbImporter
{
    /// <exception cref="TmdbImportException">The import could not run (no API key, list failed) or the embeddings failed after the movies were saved; <see cref="TmdbImportException.Result"/> has the counters of the saved part.</exception>
    Task<TmdbImportResult> ImportAsync(TmdbImportRequest request, IProgress<TmdbImportProgress>? progress, CancellationToken cancellationToken);
}

public sealed class TmdbImportException(string message, TmdbImportResult? result = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public TmdbImportResult? Result { get; } = result;
}
