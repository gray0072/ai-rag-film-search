namespace FilmSearch.Infrastructure.Search.Semantic;

/// <summary>
/// SQL of the per-dimension vector index and query (ADR-0008). pgvector can only index a column with a fixed dimension,
/// so the index is on the expression <c>vector::vector(N)</c> and covers only rows with <c>dimensions = N</c>.
/// A query uses it when it has the same expression in <c>ORDER BY</c> and the same predicate in <c>WHERE</c>,
/// so both are written here. <c>N</c> is an integer from configuration, never user text.
/// </summary>
internal static class VectorIndex
{
    /// <summary>HNSW supports at most 2000 dimensions for <c>vector</c>; larger models are searched by exact scan.</summary>
    public const int MaxIndexedDimensions = 2000;

    public static string Name(int dimensions) => $"ix_movie_embeddings_vector_{dimensions}";

    /// <summary>
    /// <c>CREATE INDEX IF NOT EXISTS</c> for one dimension: HNSW (a layered proximity graph) with cosine distance.
    /// <c>m</c> (links per node) and <c>ef_construction</c> (candidate list while building) are the pgvector defaults,
    /// written out to show the knobs.
    /// </summary>
    public static string CreateSql(int dimensions) =>
        $"CREATE INDEX IF NOT EXISTS {Name(dimensions)} ON search.movie_embeddings " +
        $"USING hnsw ((vector::vector({dimensions})) vector_cosine_ops) WITH (m = 16, ef_construction = 64) " +
        $"WHERE dimensions = {dimensions}";

    /// <summary>
    /// Cosine distance (<c>&lt;=&gt;</c>: 0 = same direction, 2 = opposite) of every profile vector of a model to
    /// <c>@query</c>. Parameters: <c>@query</c> (vector), <c>@model</c> (text).
    /// </summary>
    public static string DistanceSql(int dimensions) =>
        $"SELECT movie_id, (vector::vector({dimensions}) <=> @query) AS distance " +
        "FROM search.movie_embeddings " +
        $"WHERE dimensions = {dimensions} AND model = @model AND kind = '{nameof(EmbeddingKind.Profile)}'";
}
