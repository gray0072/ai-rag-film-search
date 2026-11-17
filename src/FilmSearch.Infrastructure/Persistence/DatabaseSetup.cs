using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FilmSearch.Infrastructure.Persistence;

public static partial class DatabaseSetup
{
    /// <summary>Name of the connection string and of the Aspire database resource.</summary>
    public const string ConnectionName = "filmsearch";

    /// <summary>Config flag: apply pending migrations on startup (on in Development and in integration tests).</summary>
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    internal static void Configure(DbContextOptionsBuilder options, string? connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "public"))
            .UseSnakeCaseNamingConvention();

    /// <summary>
    /// Registers <see cref="AppDbContext"/> and enriches it with Aspire defaults:
    /// connection retries, a DbContext health check, OpenTelemetry tracing and metrics for Npgsql.
    /// </summary>
    public static IHostApplicationBuilder AddDatabase(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionName}' is not configured.");

        builder.Services.AddDbContextPool<AppDbContext>(options => Configure(options, connectionString));
        builder.EnrichNpgsqlDbContext<AppDbContext>();

        return builder;
    }

    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseSetup));

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        LogApplyingMigrations(logger, pending.Count, pending);
        await db.Database.MigrateAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} migration(s): {Migrations}")]
    private static partial void LogApplyingMigrations(ILogger logger, int count, IReadOnlyList<string> migrations);
}
