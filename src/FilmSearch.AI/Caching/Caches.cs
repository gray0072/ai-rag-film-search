using FilmSearch.AI.Providers;
using FilmSearch.AI.QueryUnderstanding;
using FilmSearch.Core.Search;
using Microsoft.Extensions.Options;

namespace FilmSearch.AI.Caching;

/// <summary>
/// Query vectors (I19): a search query is embedded once per profile, model and normalized text (case, whitespace, ё/е),
/// not on every search, page or tool call. A cached vector saves the embedding call (tens of milliseconds locally, a
/// paid request in the cloud) and keeps searching with the same query working while the provider is down. Vectors do
/// not go stale: the key holds the model, and another model gets other keys.
/// </summary>
internal sealed class QueryEmbeddingCache(IOptions<AiOptions> options, CacheMetrics metrics)
    : LookupCache<float[]>(
        "query_embedding",
        options.Value.QueryEmbeddingCacheMaxEntries,
        TimeSpan.FromMinutes(options.Value.QueryEmbeddingCacheMinutes),
        metrics)
{
    public static string Key(EmbeddingModel model, string formattedQuery) =>
        $"{model.Profile}|{model.Model}|{model.Dimensions}|{QueryText.Normalize(formattedQuery)}";
}

/// <summary>
/// Valid query understanding results per prompt id, chat profile and normalized query (I07, bounded in I19). Fallback
/// results are not cached: after an outage the next search asks the model again.
/// </summary>
internal sealed class QueryUnderstandingCache(IOptions<QueryUnderstandingOptions> options, CacheMetrics metrics)
    : LookupCache<UnderstoodQuery>(
        "query_understanding",
        options.Value.CacheMaxEntries,
        TimeSpan.FromMinutes(options.Value.CacheMinutes),
        metrics);
