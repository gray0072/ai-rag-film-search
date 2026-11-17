using FilmSearch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>The API in-process (TestServer) against the Testcontainers database. Migrations run on startup.</summary>
public sealed class FilmSearchApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{DatabaseSetup.ConnectionName}", postgres.ConnectionString);
        builder.UseSetting(DatabaseSetup.MigrateOnStartupKey, "true");
    }
}
