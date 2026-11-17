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

    /// <summary>Key of the advisory lock held while migrations are applied.</summary>
    private const long MigrationLockKey = 0x46_53_4D_49_47; // "FSMIG"

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

    /// <summary>
    /// Applies pending migrations. A session-level advisory lock makes concurrent starts (several API replicas,
    /// parallel integration test hosts) apply them one at a time; the others then find nothing pending.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseSetup));

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({MigrationLockKey})", cancellationToken);
            try
            {
                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count == 0)
                {
                    return;
                }

                LogApplyingMigrations(logger, pending.Count, pending);
                await db.Database.MigrateAsync(cancellationToken);
            }
            finally
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({MigrationLockKey})", CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} migration(s): {Migrations}")]
    private static partial void LogApplyingMigrations(ILogger logger, int count, IReadOnlyList<string> migrations);
}
