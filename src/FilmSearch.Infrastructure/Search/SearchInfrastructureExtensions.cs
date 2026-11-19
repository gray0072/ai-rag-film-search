using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Search.Hybrid;
using FilmSearch.Infrastructure.Search.Keyword;
using FilmSearch.Infrastructure.Search.Semantic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FilmSearch.Infrastructure.Search;

public static class SearchInfrastructureExtensions
{
    /// <summary>
    /// <see cref="IMovieSearch"/> and the indexers that keep search data in sync with the catalog. Semantic search needs an
    /// <see cref="ITextEmbedder"/> (registered by <c>AddAiProviders</c>). Reindex jobs are queued through
    /// <c>AddIngestionInfrastructure</c> and run by the Worker.
    /// </summary>
    public static IServiceCollection AddSearchInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IMovieSearch, MovieSearch>();
        services.AddMetrics();
        services.AddSingleton<SearchMetrics>();

        // Keyword search (I04)
        services.AddScoped<KeywordRetriever>();
        services.AddScoped<ICatalogChangeHandler, MovieDocumentIndexer>();

        // Semantic search and embeddings (I06)
        services.AddOptions<SemanticSearchOptions>()
            .Bind(configuration.GetSection(SemanticSearchOptions.SectionName))
            .Validate(o => o.MaxResults is >= 1 and <= 1000, "Search:Semantic:MaxResults must be between 1 and 1000.")
            .ValidateOnStart();
        services.AddOptions<EmbeddingIndexOptions>()
            .Bind(configuration.GetSection(EmbeddingIndexOptions.SectionName))
            .Validate(o => o.BatchSize is >= 1 and <= 256, "Search:Embeddings:BatchSize must be between 1 and 256.")
            .ValidateOnStart();

        services.AddScoped<SemanticRetriever>();
        services.AddScoped<IMovieEmbeddingIndexer, MovieEmbeddingIndexer>();

        // Hybrid search (I07). Query understanding (IQueryUnderstanding) is registered by AddAiProviders.
        services.AddOptions<HybridSearchOptions>()
            .Bind(configuration.GetSection(HybridSearchOptions.SectionName))
            .Validate(o => o.RrfK is >= 0 and <= 1000, "Search:Hybrid:RrfK must be between 0 and 1000.")
            .Validate(o => o.CandidatesPerList is >= 1 and <= 1000, "Search:Hybrid:CandidatesPerList must be between 1 and 1000.")
            .Validate(o => o.KeywordWeight >= 0 && o.RelaxedKeywordWeight >= 0 && o.SemanticWeight >= 0 && o.SimilarTitleWeight >= 0, "Search:Hybrid weights must not be negative.")
            .Validate(o => o.MaxSimilarTitles is >= 0 and <= 10, "Search:Hybrid:MaxSimilarTitles must be between 0 and 10.")
            .ValidateOnStart();
        return services;
    }
}
