namespace FilmSearch.Core.Search;

/// <summary>
/// An embedding model as configured by an embedding profile (spec 06 §1). <see cref="Model"/> and
/// <see cref="Dimensions"/> are stored with every vector, so vectors of several models can live side by side.
/// </summary>
/// <param name="Profile">Profile name from <c>AI:EmbeddingProfiles</c>.</param>
/// <param name="Model">Model id as the provider knows it.</param>
/// <param name="Dimensions">Vector length the model returns.</param>
/// <param name="QueryPrefix">Task prefix put before a search query (part of the model's training format).</param>
/// <param name="DocumentPrefix">Task prefix put before a document.</param>
public sealed record EmbeddingModel(string Profile, string Model, int Dimensions, string QueryPrefix, string DocumentPrefix)
{
    public string FormatQuery(string query) => QueryPrefix + query;

    public string FormatDocument(string document) => DocumentPrefix + document;
}

/// <summary>
/// Turns texts into vectors with the embedding profiles of the AI layer. Search code depends on this interface;
/// the implementation in <c>FilmSearch.AI</c> uses <c>IEmbeddingGenerator</c> (ADR-0002).
/// </summary>
public interface ITextEmbedder
{
    /// <summary>The model of the active embedding profile: search queries use it.</summary>
    EmbeddingModel Active { get; }

    /// <exception cref="ArgumentException">The profile is not configured.</exception>
    EmbeddingModel GetModel(string profile);

    /// <summary>
    /// Embeds texts that are already formatted (<see cref="EmbeddingModel.FormatQuery"/> / <see cref="EmbeddingModel.FormatDocument"/>).
    /// Returns one vector per text, in order.
    /// </summary>
    /// <exception cref="AiProviderUnavailableException">The provider could not be reached or failed.</exception>
    /// <exception cref="InvalidOperationException">The provider returned vectors of a length other than <see cref="EmbeddingModel.Dimensions"/>.</exception>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(EmbeddingModel model, IReadOnlyList<string> texts, CancellationToken cancellationToken);

    /// <summary>
    /// The vector of a search query (<see cref="EmbeddingModel.FormatQuery"/> is applied here). Vectors are cached per
    /// model and normalized query text (I19), so a repeated query does not call the provider.
    /// </summary>
    /// <exception cref="AiProviderUnavailableException">The query is not cached and the provider could not be reached or failed.</exception>
    Task<ReadOnlyMemory<float>> EmbedQueryAsync(EmbeddingModel model, string query, CancellationToken cancellationToken);
}

/// <summary>An AI provider (LM Studio, OpenRouter, …) is not reachable, timed out or returned an error.</summary>
public sealed class AiProviderUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
