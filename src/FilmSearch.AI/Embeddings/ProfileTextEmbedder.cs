using FilmSearch.AI.Caching;
using FilmSearch.AI.Providers;
using FilmSearch.Core.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FilmSearch.AI.Embeddings;

/// <summary>
/// <see cref="ITextEmbedder"/> over the embedding profiles: the generator of a profile is the keyed
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> registered by <c>AddAiProviders</c>, so calls go through the same
/// telemetry and usage pipeline as chat. Provider errors become <see cref="AiProviderUnavailableException"/>,
/// so search code does not see SDK exception types. Query vectors are cached (<see cref="QueryEmbeddingCache"/>, I19).
/// </summary>
internal sealed class ProfileTextEmbedder(IOptions<AiOptions> options, IServiceProvider services, QueryEmbeddingCache queryCache) : ITextEmbedder
{
    public EmbeddingModel Active => GetModel(options.Value.ActiveEmbeddingProfile);

    public EmbeddingModel GetModel(string profile)
    {
        if (!options.Value.EmbeddingProfiles.TryGetValue(profile, out var settings))
        {
            throw new ArgumentException(
                $"Unknown embedding profile '{profile}'. Configured: {string.Join(", ", options.Value.EmbeddingProfiles.Keys)}.",
                nameof(profile));
        }

        // The configured key, not the caller's spelling: keyed services and stored data use one name per profile.
        var name = options.Value.EmbeddingProfiles.Keys.First(k => string.Equals(k, profile, StringComparison.OrdinalIgnoreCase));
        return new EmbeddingModel(name, settings.Model, settings.Dimensions, settings.QueryPrefix, settings.DocumentPrefix);
    }

    public async Task<ReadOnlyMemory<float>> EmbedQueryAsync(EmbeddingModel model, string query, CancellationToken cancellationToken)
    {
        var text = model.FormatQuery(query);
        var key = QueryEmbeddingCache.Key(model, text);
        if (queryCache.TryGet(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var vector = (await EmbedAsync(model, [text], cancellationToken))[0];
        queryCache.Set(key, vector.ToArray());
        return vector;
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        EmbeddingModel model,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var generator = services.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(model.Profile);
        GeneratedEmbeddings<Embedding<float>> embeddings;
        try
        {
            embeddings = await generator.GenerateAsync(texts, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (AiProviderErrors.IsProviderFailure(ex, cancellationToken))
        {
            throw new AiProviderUnavailableException(
                $"Embedding profile '{model.Profile}' (model {model.Model}) failed: {ex.Message}", ex);
        }

        if (embeddings.Count != texts.Count)
        {
            throw new InvalidOperationException(
                $"Embedding model {model.Model} returned {embeddings.Count} vectors for {texts.Count} texts.");
        }

        var wrong = embeddings.FirstOrDefault(e => e.Vector.Length != model.Dimensions);
        if (wrong is not null)
        {
            throw new InvalidOperationException(
                $"Embedding model {model.Model} returned vectors of {wrong.Vector.Length} dimensions; " +
                $"profile '{model.Profile}' says {model.Dimensions} (AI:EmbeddingProfiles:{model.Profile}:Dimensions).");
        }

        return [.. embeddings.Select(e => e.Vector)];
    }
}
