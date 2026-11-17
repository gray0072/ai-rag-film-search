using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Search.Keyword;
using FilmSearch.Infrastructure.Search.Semantic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FilmSearch.Infrastructure.Search;

public static class SearchInfrastructureExtensions
{
    /// <summary>
    /// <see cref="IMovieSearch"/>, the indexers that keep search data in sync with the catalog, and the background
    /// embedding jobs. Semantic search needs an <see cref="ITextEmbedder"/> (registered by <c>AddAiProviders</c>).
    /// </summary>
    public static IServiceCollection AddSearchInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IMovieSearch, MovieSearch>();

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
        services.AddSingleton<EmbeddingJobRunner>();
        services.AddSingleton<IEmbeddingJobs>(sp => sp.GetRequiredService<EmbeddingJobRunner>());
        services.AddHostedService(sp => sp.GetRequiredService<EmbeddingJobRunner>());

        return services;
    }
}
