using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Search.Keyword;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.Infrastructure.Search;

public static class SearchInfrastructureExtensions
{
    /// <summary><see cref="IMovieSearch"/> and the indexers that keep search data in sync with the catalog.</summary>
    public static IServiceCollection AddSearchInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IMovieSearch, MovieSearch>();

        // Keyword search (I04)
        services.AddScoped<KeywordRetriever>();
        services.AddScoped<ICatalogChangeHandler, MovieDocumentIndexer>();

        return services;
    }
}
