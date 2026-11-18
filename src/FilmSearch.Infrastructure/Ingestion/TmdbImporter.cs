using System.Diagnostics;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Ingestion;
using FilmSearch.Core.Localization;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Tmdb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FilmSearch.Infrastructure.Ingestion;

/// <summary>
/// TMDB import (FR-DATA-2, spec 08 §3): genres in every locale, the movie ids of a list (or the given ids), the details of
/// each movie through the rate-limited <see cref="TmdbClient"/>, mapped by <see cref="TmdbMovieMapper"/> (the seed path)
/// and upserted in batches by <see cref="ICatalogWriter"/>. Keyword documents follow inside the upsert transaction; the
/// embeddings of new and changed movies are made by a stale-only reindex at the end, so the movies are searchable in every
/// mode when the job ends. A rerun is idempotent: same TMDB ids, same rows; unchanged data is not touched.
/// </summary>
internal sealed partial class TmdbImporter(
    TmdbClient tmdb,
    ICatalogWriter writer,
    IMovieEmbeddingIndexer indexer,
    IOptions<TmdbOptions> options,
    ILogger<TmdbImporter> logger) : ITmdbImporter
{
    public const int BatchSize = 100;
    public const int MaxPages = 500;
    public const int MaxIds = 5000;
    private const int MaxErrors = 20;

    public async Task<TmdbImportResult> ImportAsync(
        TmdbImportRequest request,
        IProgress<TmdbImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        Validate(request);
        if (!options.Value.IsConfigured)
        {
            throw new TmdbImportException(TmdbServiceCollectionExtensions.MissingKeyMessage);
        }

        var stopwatch = Stopwatch.StartNew();
        var counters = new Counters();

        progress?.Report(counters.Snapshot(TmdbImportPhase.Genres, 0));
        var requests = await ImportGenresAsync(cancellationToken);

        progress?.Report(counters.Snapshot(TmdbImportPhase.Listing, 0));
        var (ids, listRequests) = await CollectIdsAsync(request, cancellationToken);
        requests += listRequests;

        foreach (var chunk in ids.Chunk(BatchSize))
        {
            var imports = new MovieImport?[chunk.Length];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, chunk.Length),
                new ParallelOptions { MaxDegreeOfParallelism = options.Value.MaxConcurrency, CancellationToken = cancellationToken },
                async (index, ct) =>
                {
                    imports[index] = await FetchAsync(chunk[index], counters, ct);
                    progress?.Report(counters.Snapshot(TmdbImportPhase.Movies, ids.Count));
                });
            requests += chunk.Length;

            var saved = await writer.UpsertMoviesAsync([.. imports.OfType<MovieImport>()], MovieSource.Tmdb, cancellationToken);
            counters.Add(saved);
            progress?.Report(counters.Snapshot(TmdbImportPhase.Movies, ids.Count));
        }

        var imported = counters.Result(ids.Count, requests, embeddings: null, stopwatch.ElapsedMilliseconds);
        if (ids.Count > 0 && counters.Failed == ids.Count)
        {
            throw new TmdbImportException($"Every movie request failed. First error: {(counters.Errors is [var first, ..] ? first : "none")}", imported);
        }

        ReindexResult embeddings;
        try
        {
            var reindexProgress = new ReportOnCallingThread<ReindexProgress>(p =>
                progress?.Report(counters.Snapshot(TmdbImportPhase.Embeddings, ids.Count, p)));
            embeddings = await indexer.ReindexAsync(new ReindexRequest(), reindexProgress, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TmdbImportException(
                $"The movies were saved, but embedding them failed: {ex.Message} Keyword search finds them already; start a reindex "
                + "(POST /api/admin/embeddings/reindex) when the embedding provider is back.",
                imported,
                ex);
        }

        var result = counters.Result(ids.Count, requests, embeddings, stopwatch.ElapsedMilliseconds);
        LogImported(logger, result.Movies, result.Created, result.Updated, result.Unchanged, result.Skipped, result.NotFound, result.Failed, embeddings.Embedded, result.ElapsedMilliseconds);
        return result;
    }

    /// <summary>The admin endpoint validates too; this guards jobs queued by other paths (schedule, tests).</summary>
    internal static void Validate(TmdbImportRequest request)
    {
        var hasIds = request.TmdbIds is { Count: > 0 };
        if (hasIds == (request.List is not null))
        {
            throw new TmdbImportException("A TMDB import needs either a list with pages or TMDB ids, not both.");
        }

        if (request.List is not null && request.Pages is < 1 or > MaxPages)
        {
            throw new TmdbImportException($"Pages must be between 1 and {MaxPages}.");
        }

        if (hasIds && (request.TmdbIds!.Count > MaxIds || request.TmdbIds.Any(id => id <= 0)))
        {
            throw new TmdbImportException($"TMDB ids must be positive, at most {MaxIds} of them.");
        }
    }

    private async Task<int> ImportGenresAsync(CancellationToken cancellationToken)
    {
        var lists = new Dictionary<string, TmdbGenreList>(StringComparer.Ordinal);
        foreach (var locale in Locales.All)
        {
            lists[locale] = await tmdb.GetGenresAsync(locale, cancellationToken);
        }

        await writer.UpsertGenresAsync(TmdbMovieMapper.ToGenres(lists), cancellationToken);
        return lists.Count;
    }

    private async Task<(IReadOnlyList<int> Ids, int Requests)> CollectIdsAsync(TmdbImportRequest request, CancellationToken cancellationToken)
    {
        if (request.List is not { } list)
        {
            return ([.. request.TmdbIds!.Distinct()], 0);
        }

        var ids = new List<int>();
        var seen = new HashSet<int>();
        var requests = 0;
        for (var page = 1; page <= request.Pages; page++)
        {
            var result = await tmdb.GetListPageAsync(list, page, cancellationToken);
            requests++;

            // A list moves while it is paged (popularity changes): a movie can show up twice, or on no page.
            ids.AddRange(result.Results.Where(m => !m.Adult && seen.Add(m.Id)).Select(m => m.Id));
            if (page >= result.TotalPages)
            {
                break;
            }
        }

        return (ids, requests);
    }

    /// <summary>One movie's details, mapped; null when TMDB does not know it, it is adult or untitled, or the request failed.</summary>
    private async Task<MovieImport?> FetchAsync(int tmdbId, Counters counters, CancellationToken cancellationToken)
    {
        try
        {
            var details = await tmdb.GetMovieAsync(tmdbId, cancellationToken);
            if (details is null)
            {
                counters.NotFoundOne();
                return null;
            }

            if (details.Adult || (string.IsNullOrWhiteSpace(details.Title) && string.IsNullOrWhiteSpace(details.OriginalTitle)))
            {
                counters.SkippedOne();
                return null;
            }

            return TmdbMovieMapper.ToImport(details);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Retries are done by the HTTP pipeline; a movie that still fails does not stop the others.
            counters.FailedOne($"{tmdbId}: {ex.Message}");
            LogMovieFailed(logger, ex, tmdbId);
            return null;
        }
        finally
        {
            counters.FetchedOne();
        }
    }

    private sealed class Counters
    {
        private readonly Lock _lock = new();
        private readonly List<string> _errors = [];
        private int _fetched;
        private int _skipped;
        private int _notFound;
        private int _failed;
        private UpsertResult _saved = UpsertResult.Empty;

        public int Failed => Volatile.Read(ref _failed);

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (_lock)
                {
                    return [.. _errors];
                }
            }
        }

        public void FetchedOne() => Interlocked.Increment(ref _fetched);

        public void SkippedOne() => Interlocked.Increment(ref _skipped);

        public void NotFoundOne() => Interlocked.Increment(ref _notFound);

        public void FailedOne(string error)
        {
            Interlocked.Increment(ref _failed);
            lock (_lock)
            {
                if (_errors.Count < MaxErrors)
                {
                    _errors.Add(error);
                }
            }
        }

        public void Add(UpsertResult saved)
        {
            lock (_lock)
            {
                _saved = _saved.Add(saved);
            }
        }

        public TmdbImportProgress Snapshot(TmdbImportPhase phase, int movies, ReindexProgress? embeddings = null)
        {
            lock (_lock)
            {
                return new TmdbImportProgress(
                    phase, movies, Volatile.Read(ref _fetched), _saved.Created, _saved.Updated, _saved.Unchanged,
                    Volatile.Read(ref _skipped), Volatile.Read(ref _notFound), Volatile.Read(ref _failed), embeddings);
            }
        }

        public TmdbImportResult Result(int movies, int requests, ReindexResult? embeddings, long elapsedMs)
        {
            lock (_lock)
            {
                return new TmdbImportResult(
                    movies, _saved.Created, _saved.Updated, _saved.Unchanged, Volatile.Read(ref _skipped), Volatile.Read(ref _notFound),
                    Volatile.Read(ref _failed), [.. _errors], requests, embeddings, elapsedMs);
            }
        }
    }

    private sealed class ReportOnCallingThread<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "TMDB import of {Movies} movies: {Created} created, {Updated} updated, {Unchanged} unchanged, {Skipped} skipped, {NotFound} not found, {Failed} failed; {Embedded} texts embedded; {ElapsedMs} ms")]
    private static partial void LogImported(ILogger logger, int movies, int created, int updated, int unchanged, int skipped, int notFound, int failed, int embedded, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TMDB movie {TmdbId} could not be imported")]
    private static partial void LogMovieFailed(ILogger logger, Exception exception, int tmdbId);
}
