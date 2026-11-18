using FilmSearch.Core.Ingestion;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Commerce;
using FilmSearch.Infrastructure.Ingestion.Jobs;
using FilmSearch.Infrastructure.Tmdb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FilmSearch.Infrastructure.Ingestion;

public static class IngestionInfrastructureExtensions
{
    /// <summary>The job queue (<see cref="IBackgroundJobs"/>) and reindex jobs on top of it (<see cref="IEmbeddingJobs"/>): what the API needs to queue and inspect jobs.</summary>
    public static IServiceCollection AddIngestionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<JobOptions>()
            .Bind(configuration.GetSection(JobOptions.SectionName))
            .Validate(o => o.PollIntervalMs is >= 50 and <= 60_000, "Jobs:PollIntervalMs must be between 50 and 60000.")
            .Validate(o => o.HeartbeatIntervalMs is >= 50 and <= 60_000, "Jobs:HeartbeatIntervalMs must be between 50 and 60000.")
            .Validate(o => o.LeaseSeconds * 1000 >= 3 * o.HeartbeatIntervalMs, "Jobs:LeaseSeconds must cover at least three heartbeats.")
            .Validate(o => o.MaxAttempts is >= 1 and <= 10, "Jobs:MaxAttempts must be between 1 and 10.")
            .ValidateOnStart();

        services.TryAddScoped<JobStore>();
        services.TryAddScoped<IBackgroundJobs>(sp => sp.GetRequiredService<JobStore>());
        services.TryAddScoped<IEmbeddingJobs, EmbeddingJobs>();
        return services;
    }

    /// <summary>
    /// The Worker's background work (I16): the job loop with its handlers (TMDB import, embedding reindex), the TMDB
    /// client, the daily import schedule and the order expiry job. Needs the catalog, search and commerce infrastructure
    /// and the AI providers registered too.
    /// </summary>
    public static IServiceCollection AddBackgroundWork(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIngestionInfrastructure(configuration);
        services.AddTmdbClient(configuration);
        services.AddScoped<ITmdbImporter, TmdbImporter>();

        services.AddKeyedScoped<IJobHandler, TmdbImportJobHandler>(JobKind.TmdbImport);
        services.AddKeyedScoped<IJobHandler, EmbeddingReindexJobHandler>(JobKind.EmbeddingReindex);
        services.AddHostedService<JobWorker>();

        services.AddOptions<TmdbScheduleOptions>()
            .Bind(configuration.GetSection(TmdbScheduleOptions.SectionName))
            .Validate(o => o.Pages is >= 1 and <= TmdbImporter.MaxPages, $"Tmdb:Schedule:Pages must be between 1 and {TmdbImporter.MaxPages}.")
            .ValidateOnStart();
        services.AddHostedService<TmdbImportScheduler>();

        services.AddOrderExpiryService();
        return services;
    }
}
