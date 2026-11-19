using System.Globalization;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Localization;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Catalog;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;

namespace FilmSearch.Infrastructure.Search.Semantic;

/// <summary>A movie found by vector search. <see cref="Rank"/> starts at 1; <see cref="Similarity"/> is cosine similarity (1 − cosine distance).</summary>
internal sealed record VectorHit(Guid MovieId, int Rank, double Similarity);

/// <summary>Row of <see cref="VectorIndex.DistanceSql"/>.</summary>
internal sealed record VectorDistance(Guid MovieId, double Distance);

/// <summary>
/// Semantic retrieval (spec 05 §3), the "dense" half of hybrid search: the query is embedded with the active profile's
/// model (query prefix included) and compared with the stored movie vectors of the same model by cosine distance.
/// Catalog filters are applied inside the same SQL query (pre-filtering): with HNSW, pgvector filters while it walks the
/// graph, and iterative scans keep walking until enough rows pass the filter, so a strict filter does not empty the list.
/// With vectors in several locales, a movie's best vector counts.
/// </summary>
internal sealed class SemanticRetriever(AppDbContext db, ITextEmbedder embedder, IOptions<SemanticSearchOptions> options)
{
    /// <summary>Length of a semantic result list (<c>Search:Semantic:MaxResults</c>).</summary>
    public int MaxResults => options.Value.MaxResults;

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(string text, MovieFilter filter, CancellationToken cancellationToken)
    {
        var vector = await EmbedQueryAsync(text, cancellationToken);
        return await NearestAsync(vector, filter, MaxResults, cancellationToken);
    }

    /// <summary>The query vector of the active model (query prefix included; cached, I19). Calls the provider only, not the database.</summary>
    /// <exception cref="AiProviderUnavailableException">The embedding provider failed.</exception>
    public async Task<ReadOnlyMemory<float>> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        // The provider call is a GenAI embeddings span inside this one; a cached vector has none.
        using var activity = SearchTelemetry.Activities.StartActivity("search.embed_query");
        var model = embedder.Active;
        activity?.SetTag("filmsearch.search.embedding_model", model.Model);
        return await embedder.EmbedQueryAsync(model, text, cancellationToken);
    }

    /// <summary>Movies nearest to <paramref name="vector"/> (a vector of the active model), best first.</summary>
    public Task<IReadOnlyList<VectorHit>> NearestAsync(ReadOnlyMemory<float> vector, MovieFilter filter, int limit, CancellationToken cancellationToken) =>
        NearestAsync(embedder.Active, vector, filter, excludeMovieId: null, limit, cancellationToken);

    /// <summary>Neighbours of a movie's own vector (English first, when it has several). Empty when the movie has no vector of the active model.</summary>
    public async Task<IReadOnlyList<VectorHit>> SimilarAsync(Guid movieId, MovieFilter filter, int limit, CancellationToken cancellationToken)
    {
        var own = await GetMovieVectorsAsync([movieId], cancellationToken);
        return own.TryGetValue(movieId, out var vector)
            ? await NearestAsync(embedder.Active, vector, filter, movieId, limit, cancellationToken)
            : [];
    }

    /// <summary>The stored vectors of the active model for these movies (English first, when a movie has several). Movies without a vector are missing.</summary>
    public async Task<IReadOnlyDictionary<Guid, ReadOnlyMemory<float>>> GetMovieVectorsAsync(IReadOnlyList<Guid> movieIds, CancellationToken cancellationToken)
    {
        if (movieIds.Count == 0)
        {
            return new Dictionary<Guid, ReadOnlyMemory<float>>();
        }

        var model = embedder.Active;
        var rows = await db.Set<MovieEmbedding>()
            .AsNoTracking()
            .Where(e => movieIds.Contains(e.MovieId) && e.Model == model.Model && e.Kind == EmbeddingKind.Profile && e.Dimensions == model.Dimensions)
            .Select(e => new { e.MovieId, e.Locale, e.Vector })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.MovieId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.Locale == Locales.English ? 0 : 1).ThenBy(r => r.Locale, StringComparer.Ordinal).First().Vector.Memory);
    }

    private async Task<IReadOnlyList<VectorHit>> NearestAsync(
        EmbeddingModel model,
        ReadOnlyMemory<float> query,
        MovieFilter filter,
        Guid? excludeMovieId,
        int limit,
        CancellationToken cancellationToken)
    {
        using var activity = SearchTelemetry.Activities.StartActivity("search.vector");
        activity?.SetTag("filmsearch.search.embedding_model", model.Model);
        activity?.SetTag("filmsearch.search.limit", limit);
        var strategy = db.Database.CreateExecutionStrategy();
        var rows = await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // A movie has one row per indexed locale (one by default): take enough rows to keep `limit` movies after
            // keeping the best row per movie. Index-only scan of (model, locale).
            var localeCount = await db.Set<MovieEmbedding>()
                .Where(e => e.Model == model.Model && e.Kind == EmbeddingKind.Profile)
                .Select(e => e.Locale)
                .Distinct()
                .CountAsync(ct);
            var rowLimit = limit * Math.Max(localeCount, 1);

            // HNSW returns at most ef_search candidates per scan (default 40). Iterative scan (pgvector 0.8) continues
            // the walk when filters removed too many of them; relaxed order may return rows slightly out of order,
            // they are sorted again below. set_config(..., true) is SET LOCAL: it lasts until the end of this transaction.
            var efSearch = Math.Clamp(rowLimit, 40, 1000).ToString(CultureInfo.InvariantCulture);
            await db.Database.ExecuteSqlAsync(
                $"SELECT set_config('hnsw.ef_search', {efSearch}, true), set_config('hnsw.iterative_scan', 'relaxed_order', true)",
                ct);

            var result = await BuildQuery(model, query, filter, excludeMovieId, rowLimit).ToListAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);

        IReadOnlyList<VectorHit> hits = [.. rows
            .GroupBy(r => r.MovieId)
            .Select(g => (MovieId: g.Key, Distance: g.Min(r => r.Distance)))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.MovieId)
            .Take(limit)
            .Select((x, i) => new VectorHit(x.MovieId, i + 1, Math.Round(1 - x.Distance, 6)))];
        activity?.SetTag("filmsearch.search.hits", hits.Count);
        return hits;
    }

    /// <summary>
    /// The vector query: <see cref="VectorIndex.DistanceSql"/> composed with LINQ filters, ordered by distance.
    /// PostgreSQL flattens the raw SQL subquery, so <c>ORDER BY distance LIMIT n</c> can use the HNSW index.
    /// Internal for the test that checks the plan.
    /// </summary>
    internal IQueryable<VectorDistance> BuildQuery(
        EmbeddingModel model,
        ReadOnlyMemory<float> query,
        MovieFilter filter,
        Guid? excludeMovieId,
        int rowLimit)
    {
        var rows = db.Database.SqlQueryRaw<VectorDistance>(
            VectorIndex.DistanceSql(model.Dimensions),
            new NpgsqlParameter("query", new Vector(query)),
            new NpgsqlParameter("model", model.Model));

        if (excludeMovieId is { } excluded)
        {
            rows = rows.Where(r => r.MovieId != excluded);
        }

        if (filter.HasConditions())
        {
            var allowed = db.Set<Movie>().AsNoTracking().ApplyFilter(filter).Select(m => m.Id);
            rows = rows.Where(r => allowed.Contains(r.MovieId));
        }

        return rows.OrderBy(r => r.Distance).Take(rowLimit);
    }
}
