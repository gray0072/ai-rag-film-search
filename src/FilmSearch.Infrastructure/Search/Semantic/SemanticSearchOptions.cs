namespace FilmSearch.Infrastructure.Search.Semantic;

/// <summary>Configuration section <c>Search:Semantic</c>.</summary>
public sealed class SemanticSearchOptions
{
    public const string SectionName = "Search:Semantic";

    /// <summary>
    /// Length of the semantic result list. Every movie has some similarity to any query, so the list is cut here;
    /// the <c>total</c> of a semantic search is at most this number.
    /// </summary>
    public int MaxResults { get; set; } = 100;
}

/// <summary>Configuration section <c>Search:Embeddings</c>.</summary>
public sealed class EmbeddingIndexOptions
{
    public const string SectionName = "Search:Embeddings";

    /// <summary>
    /// Locales to embed movie texts in. Default <c>en</c> only: a multilingual model maps a Russian or Swedish query
    /// close to the English text. More locales are an experiment (I06, I08); search takes the best vector per movie.
    /// Null by default because the configuration binder appends to a non-empty default list.
    /// </summary>
    public List<string>? Locales { get; set; }

    /// <summary>Texts per embedding call.</summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>Queue a reindex of stale vectors on startup, after the seed (Development).</summary>
    public bool IndexOnStartup { get; set; }

    public IReadOnlyList<string> ResolveLocales() =>
        Locales is { Count: > 0 } ? Locales : [FilmSearch.Core.Localization.Locales.English];
}
