using FilmSearch.Core.Catalog;
using FilmSearch.Core.Ingestion;
using FilmSearch.Infrastructure.Ingestion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FilmSearch.Infrastructure.Catalog;

public static class CatalogInfrastructureExtensions
{
    /// <summary>Catalog reader and writer over EF Core, and the seed loader.</summary>
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ICatalogReader, CatalogReader>();
        services.AddScoped<ICatalogWriter, CatalogWriter>();
        services.AddScoped<ICatalogSeeder, CatalogSeeder>();

        services.AddOptions<CatalogSeedOptions>()
            .Bind(configuration.GetSection(CatalogSeedOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.FilePath), "Catalog:Seed:FilePath is required.")
            .Validate(o => o.BatchSize is >= 1 and <= 1000, "Catalog:Seed:BatchSize must be between 1 and 1000.")
            .ValidateOnStart();

        return services;
    }
}
