using System.Diagnostics;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Localization;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;

namespace FilmSearch.Infrastructure.Search.Semantic;

/// <summary>
/// The embeddings pipeline (spec 08 §4): walks the catalog in batches, builds each movie's embedding text per locale
/// (<see cref="MovieEmbeddingText"/>), and embeds only texts whose hash differs from the stored vector's
/// <see cref="MovieEmbedding.ContentHash"/>. Each batch is saved on its own, so a failure keeps the work done so far and
/// the next run continues where it stopped. Vectors are keyed by model: indexing a second model leaves the first one's
/// vectors in place, and search keeps using the active profile's model until the configuration is switched (FR-DATA-4).
/// </summary>
internal sealed partial class MovieEmbeddingIndexer(
    AppDbContext db,
    ICatalogReader catalog,
    ITextEmbedder embedder,
    IOptions<EmbeddingIndexOptions> options,
    TimeProvider time,
    ILogger<MovieEmbeddingIndexer> logger) : IMovieEmbeddingIndexer
{
    internal static readonly ActivitySource ActivitySource = new("FilmSearch");

    public async Task<ReindexResult> ReindexAsync(ReindexRequest request, IProgress<ReindexProgress>? progress, CancellationToken cancellationToken)
    {
        var model = request.Profile is null ? embedder.Active : embedder.GetModel(request.Profile);
        var locales = ResolveLocales(request.Locales);
        var localeList = string.Join(",", locales);
        var stopwatch = Stopwatch.StartNew();

        using var activity = ActivitySource.StartActivity("embeddings.reindex");
        activity?.SetTag("filmsearch.embedding.model", model.Model);
        activity?.SetTag("filmsearch.embedding.dimensions", model.Dimensions);
        activity?.SetTag("filmsearch.embedding.locales", localeList);
        activity?.SetTag("filmsearch.embedding.only_stale", request.OnlyStale);

        await EnsureVectorIndexAsync(model.Dimensions, cancellationToken);

        // Vectors of locales that are no longer indexed would still be found by search: remove them.
        var deleted = await db.Set<MovieEmbedding>()
            .Where(e => e.Model == model.Model && e.Kind == EmbeddingKind.Profile && !locales.Contains(e.Locale))
            .ExecuteDeleteAsync(cancellationToken);

        var movieIds = await catalog.GetMovieIdsAsync(cancellationToken);
        var stored = await db.Set<MovieEmbedding>()
            .AsNoTracking()
            .Where(e => e.Model == model.Model && e.Kind == EmbeddingKind.Profile)
            .Select(e => new { e.MovieId, e.Locale, e.ContentHash, e.Dimensions })
            .ToDictionaryAsync(e => (e.MovieId, e.Locale), cancellationToken);

        var total = movieIds.Count * locales.Count;
        int processed = 0, embedded = 0, unchanged = 0;
        var batchSize = options.Value.BatchSize;

        foreach (var chunk in movieIds.Chunk(batchSize))
        {
            var pending = new List<PendingText>();
            foreach (var locale in locales)
            {
                foreach (var movie in await catalog.GetMovieDetailsAsync(chunk, locale, cancellationToken))
                {
                    var text = model.FormatDocument(MovieEmbeddingText.Build(movie));
                    var hash = MovieEmbeddingText.Hash(text);
                    var current = stored.TryGetValue((movie.Id, locale), out var row)
                        && row.ContentHash == hash
                        && row.Dimensions == model.Dimensions;

                    if (request.OnlyStale && current)
                    {
                        unchanged++;
                    }
                    else
                    {
                        pending.Add(new PendingText(movie.Id, locale, text, hash));
                    }
                }
            }

            foreach (var batch in pending.Chunk(batchSize))
            {
                var vectors = await embedder.EmbedAsync(model, [.. batch.Select(p => p.Text)], cancellationToken);
                await SaveAsync(model, batch, vectors, cancellationToken);
                embedded += batch.Length;
            }

            processed += chunk.Length * locales.Count;
            progress?.Report(new ReindexProgress(total, processed, embedded, unchanged));
        }

        if (embedded > 0 || deleted > 0)
        {
            // VACUUM: re-embedded and deleted rows stay in the HNSW graph as dead entries until vacuum removes them; a scan
            // spends its ef_search candidates on them and returns fewer rows (I06 experiment: 4.8 of 10 rows at
            // ef_search = 10 after deleting two thirds of the vectors). ANALYZE: right after a bulk load the planner still
            // thinks the table is nearly empty. Autovacuum would do both later on its own. Not inside a transaction.
            await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) search.movie_embeddings", cancellationToken);
        }

        var result = new ReindexResult(
            model.Profile, model.Model, model.Dimensions, locales, movieIds.Count, embedded, unchanged, deleted, stopwatch.ElapsedMilliseconds);

        activity?.SetTag("filmsearch.embedding.embedded", embedded);
        activity?.SetTag("filmsearch.embedding.unchanged", unchanged);
        var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
        LogReindexed(logger, model.Model, localeList, embedded, unchanged, deleted, result.ElapsedMilliseconds, embedded / seconds);
        return result;
    }

    public async Task<IReadOnlyList<EmbeddingSetStats>> GetStatsAsync(CancellationToken cancellationToken) =>
        await db.Set<MovieEmbedding>()
            .AsNoTracking()
            .GroupBy(e => new { e.Model, e.Dimensions, e.Locale })
            .OrderBy(g => g.Key.Model)
            .ThenBy(g => g.Key.Locale)
            .Select(g => new EmbeddingSetStats(g.Key.Model, g.Key.Dimensions, g.Key.Locale, g.Count()))
            .ToListAsync(cancellationToken);

    public async Task<int> DeleteModelAsync(string model, CancellationToken cancellationToken)
    {
        if (string.Equals(model, embedder.Active.Model, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Model '{model}' belongs to the active embedding profile '{embedder.Active.Profile}': semantic search uses its vectors.");
        }

        var deleted = await db.Set<MovieEmbedding>().Where(e => e.Model == model).ExecuteDeleteAsync(cancellationToken);
        LogDeleted(logger, model, deleted);
        return deleted;
    }

    /// <summary>Creates the HNSW index of a dimension the first time a model of that size is indexed (ADR-0008).</summary>
    private async Task EnsureVectorIndexAsync(int dimensions, CancellationToken cancellationToken)
    {
        if (dimensions > VectorIndex.MaxIndexedDimensions)
        {
            LogNoIndex(logger, dimensions, VectorIndex.MaxIndexedDimensions);
            return;
        }

        var sql = VectorIndex.CreateSql(dimensions);
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private async Task SaveAsync(
        EmbeddingModel model,
        IReadOnlyList<PendingText> batch,
        IReadOnlyList<ReadOnlyMemory<float>> vectors,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var movieIds = batch.Select(p => p.MovieId).Distinct().ToList();
        var existing = await db.Set<MovieEmbedding>()
            .Where(e => movieIds.Contains(e.MovieId) && e.Model == model.Model && e.Kind == EmbeddingKind.Profile)
            .ToDictionaryAsync(e => (e.MovieId, e.Locale), cancellationToken);

        for (var i = 0; i < batch.Count; i++)
        {
            var pending = batch[i];
            var vector = new Vector(vectors[i]);
            if (existing.TryGetValue((pending.MovieId, pending.Locale), out var row))
            {
                row.Dimensions = model.Dimensions;
                row.Text = pending.Text;
                row.ContentHash = pending.Hash;
                row.Vector = vector;
                row.CreatedAt = now;
            }
            else
            {
                db.Add(new MovieEmbedding
                {
                    Id = Guid.CreateVersion7(now),
                    MovieId = pending.MovieId,
                    Model = model.Model,
                    Dimensions = model.Dimensions,
                    Kind = EmbeddingKind.Profile,
                    Locale = pending.Locale,
                    Text = pending.Text,
                    ContentHash = pending.Hash,
                    Vector = vector,
                    CreatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    private List<string> ResolveLocales(IReadOnlyList<string>? requested)
    {
        var locales = (requested is { Count: > 0 } ? requested : options.Value.ResolveLocales())
            .Select(l => l.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var unknown = locales.Where(l => !Locales.All.Contains(l)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"Unknown locale(s): {string.Join(", ", unknown)}. Supported: {string.Join(", ", Locales.All)}.",
                nameof(requested));
        }

        return locales;
    }

    private sealed record PendingText(Guid MovieId, string Locale, string Text, string Hash);

    [LoggerMessage(Level = LogLevel.Information, Message = "Embeddings of {Model} ({Locales}): {Embedded} embedded, {Unchanged} unchanged, {Deleted} deleted in {ElapsedMs} ms ({TextsPerSecond:F1} texts/s)")]
    private static partial void LogReindexed(ILogger logger, string model, string locales, int embedded, int unchanged, int deleted, long elapsedMs, double textsPerSecond);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} vectors of model {Model}")]
    private static partial void LogDeleted(ILogger logger, string model, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vectors of {Dimensions} dimensions are not indexed (HNSW supports up to {Max}); search uses an exact scan")]
    private static partial void LogNoIndex(ILogger logger, int dimensions, int max);
}
