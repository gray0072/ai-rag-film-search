using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>
/// A deterministic fake embedding model: a hashed bag of words ("feature hashing"). Each word adds ±1 to one of
/// <c>dimensions</c> buckets, and the vector is normalized, so the cosine similarity of two texts grows with the words
/// they share. Enough to test the pipeline and ranking mechanics without LM Studio; it knows nothing about meaning or
/// languages, so cross-language checks are manual, with the real model.
/// </summary>
public sealed partial class HashingEmbeddingGenerator(int dimensions) : IEmbeddingGenerator<string, Embedding<float>>
{
    // Words every text has (template labels, task prefixes) or that carry no topic.
    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "for", "his", "her", "who", "with", "from", "that", "this", "into", "their", "they", "when", "after",
        "title", "genres", "director", "cast", "keywords", "tagline", "overview", "original",
        "task", "search", "result", "query", "none", "text", "passage", "movie", "film",
    ];

    private int _texts;

    /// <summary>Number of texts embedded so far.</summary>
    public int Texts => _texts;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = values.Select(v => new Embedding<float>(Embed(v))).ToList();
        Interlocked.Add(ref _texts, embeddings.Count);
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public float[] Embed(string text)
    {
        var vector = new float[dimensions];
        foreach (Match match in Word().Matches(text.ToLowerInvariant()))
        {
            var word = match.Value;
            if (word.Length < 3 || StopWords.Contains(word))
            {
                continue;
            }

            // Crude stemming so "dreams" and "dream" meet.
            if (word.Length > 4 && word.EndsWith('s'))
            {
                word = word[..^1];
            }

            var hash = Fnv1a(word);
            vector[(int)(hash % (uint)dimensions)] += (hash & 0x8000_0000) == 0 ? 1f : -1f;
        }

        var norm = MathF.Sqrt(vector.Sum(x => x * x));
        if (norm == 0)
        {
            vector[0] = 1;
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    /// <summary>FNV-1a: stable across processes, unlike <see cref="string.GetHashCode()"/>.</summary>
    private static uint Fnv1a(string word)
    {
        var hash = 2166136261u;
        foreach (var c in word)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash;
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
}
