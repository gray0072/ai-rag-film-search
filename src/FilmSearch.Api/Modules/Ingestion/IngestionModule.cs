using FilmSearch.Infrastructure.Ingestion;

namespace FilmSearch.Api.Modules.Ingestion;

/// <summary>
/// Ingestion in the API (I16): the job queue only. The API queues TMDB imports and reindex jobs (admin endpoints, the
/// startup reindex in Development) and reads their state; the Worker runs them (spec 08 §3).
/// </summary>
public static class IngestionModule
{
    public static IServiceCollection AddIngestionModule(this IServiceCollection services, IConfiguration configuration) =>
        services.AddIngestionInfrastructure(configuration);
}
