using Pgvector;

namespace FilmSearch.Infrastructure.Search.Semantic;

/// <summary>What part of a movie a vector represents. Only the whole-movie profile for now; plot or review chunks may come later.</summary>
internal enum EmbeddingKind
{
    Profile = 0,
}

/// <summary>
/// One vector of a movie (spec 03 "Search"): one row per movie, model, kind and locale. The <see cref="Vector"/> column
/// has no fixed dimension, so models of different sizes share the table; each dimension gets its own partial HNSW index
/// (ADR-0008, <see cref="VectorIndex"/>).
/// </summary>
internal sealed class MovieEmbedding
{
    public Guid Id { get; set; }

    public Guid MovieId { get; set; }

    /// <summary>Model id of the embedding profile that produced the vector.</summary>
    public required string Model { get; set; }

    public int Dimensions { get; set; }

    public EmbeddingKind Kind { get; set; }

    /// <summary>Locale of the movie texts that were embedded (<c>en</c>, <c>ru</c>, <c>sv</c>).</summary>
    public required string Locale { get; set; }

    /// <summary>The exact text sent to the model, document prefix included.</summary>
    public required string Text { get; set; }

    /// <summary>Hash of <see cref="Text"/> (<c>MovieEmbeddingText.Hash</c>): the vector is current while it equals the hash of the text built now.</summary>
    public required string ContentHash { get; set; }

    public required Vector Vector { get; set; }

    /// <summary>When the vector was computed (updated on every re-embedding).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
