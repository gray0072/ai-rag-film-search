using FilmSearch.Api.Modules.Admin;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>
/// The API in-process (TestServer) against the Testcontainers database. Migrations run on startup; admin endpoints
/// are on; AI health checks are off; the seed file is not loaded on startup (tests that need the catalog use <see cref="CatalogApiFixture"/>).
/// </summary>
public sealed class FilmSearchApiFactory(PostgresFixture postgres, string? connectionString = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{DatabaseSetup.ConnectionName}", connectionString ?? postgres.ConnectionString);
        builder.UseSetting(DatabaseSetup.MigrateOnStartupKey, "true");
        builder.UseSetting(AdminModule.EnabledKey, "true");
        // Tests never call a real model (AGENTS.md): no LM Studio health probe, AI clients are faked per test.
        builder.UseSetting("AI:HealthChecksEnabled", "false");
    }
}
