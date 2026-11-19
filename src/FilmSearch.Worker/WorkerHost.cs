using FilmSearch.AI.Providers;
using FilmSearch.Core.Owners;
using FilmSearch.Infrastructure.Catalog;
using FilmSearch.Infrastructure.Commerce;
using FilmSearch.Infrastructure.Ingestion;
using FilmSearch.Infrastructure.Persistence;
using FilmSearch.Infrastructure.Search;

namespace FilmSearch.Worker;

/// <summary>
/// Composition of the Worker, shared by <c>Program.cs</c> and the integration tests (which host it in-process).
/// The modules are registered with the same extension methods as in the API; only the background services differ.
/// </summary>
public static class WorkerHost
{
    /// <summary>
    /// A host builder whose content root is the output directory: the API's appsettings files are copied there
    /// (see the project file), so <c>dotnet run</c> and the AppHost read the same module configuration as the API.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(string[] args, string? environmentName = null) =>
        Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = environmentName,
            ApplicationName = typeof(WorkerHost).Assembly.GetName().Name,
        });

    public static IHostApplicationBuilder AddWorkerServices(this IHostApplicationBuilder builder)
    {
        builder.AddServiceDefaults();
        builder.AddDatabase();
        builder.AddAiProviders();

        var configuration = builder.Configuration;
        builder.Services.AddCatalogInfrastructure(configuration);
        builder.Services.AddSearchInfrastructure(configuration);
        builder.Services.AddCommerceInfrastructure(configuration);
        builder.Services.AddBackgroundWork(configuration);

        // Commerce registers owner-scoped services (cart, checkout); the Worker never uses them and has no owner.
        builder.Services.AddScoped<IOwnerContext, NoOwnerContext>();
        return builder;
    }

    /// <summary>The first argument <c>migrate</c>: apply pending migrations and exit (the deployment's migration step, I20).</summary>
    public static bool IsMigrateCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "migrate", StringComparison.OrdinalIgnoreCase);

    /// <summary>Applies migrations when <c>Database:MigrateOnStartup</c> is on (Development): the Worker may start before the API.</summary>
    public static async Task MigrateOnStartupAsync(this IHost host)
    {
        if (host.Services.GetRequiredService<IConfiguration>().GetValue<bool>(DatabaseSetup.MigrateOnStartupKey))
        {
            await host.Services.MigrateDatabaseAsync();
        }
    }

    private sealed class NoOwnerContext : IOwnerContext
    {
        public Guid OwnerId => throw new InvalidOperationException("The Worker has no owner: user-scoped services are not available here.");
    }
}
