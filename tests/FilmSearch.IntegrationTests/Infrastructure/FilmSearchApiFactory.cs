using FilmSearch.AI.Providers;
using FilmSearch.Api.Modules.Admin;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>
/// The API in-process (TestServer) against the Testcontainers database. Migrations run on startup; admin endpoints
/// are on; AI health checks are off; the seed file is not loaded on startup (tests that need the catalog use <see cref="CatalogApiFixture"/>).
/// Every embedding profile is served by a <see cref="HashingEmbeddingGenerator"/> of the profile's dimension, so no test
/// calls a real model. <paramref name="settings"/> are extra configuration values.
/// </summary>
public sealed class FilmSearchApiFactory(
    PostgresFixture postgres,
    string? connectionString = null,
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{DatabaseSetup.ConnectionName}", connectionString ?? postgres.ConnectionString);
        builder.UseSetting(DatabaseSetup.MigrateOnStartupKey, "true");
        builder.UseSetting(AdminModule.EnabledKey, "true");
        // Tests never call a real model (AGENTS.md): no LM Studio health probe, AI clients are faked.
        builder.UseSetting("AI:HealthChecksEnabled", "false");
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        // Replaces the provider-backed generators: a later AnyKey registration wins over the one of AddAiProviders.
        builder.ConfigureTestServices(services =>
            services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(KeyedService.AnyKey, (sp, key) =>
                new HashingEmbeddingGenerator(sp.GetRequiredService<IOptions<AiOptions>>().Value.GetEmbeddingProfile((string)key!).Dimensions)));
    }
}
