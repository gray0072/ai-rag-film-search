using System.Net;
using System.Net.Http.Json;
using FilmSearch.Api.Modules.AppInfo;
using FilmSearch.Api.Owners;
using FilmSearch.Infrastructure.Persistence;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.IntegrationTests;

public sealed class ApiSmokeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly FilmSearchApiFactory _factory = new(postgres);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_returns_200_when_database_is_reachable()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Migrations_create_vector_and_pg_trgm_extensions()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var extensions = await db.Database
            .SqlQuery<string>($"SELECT extname AS \"Value\" FROM pg_extension")
            .ToListAsync(Ct);

        extensions.ShouldContain("vector");
        extensions.ShouldContain("pg_trgm");
        (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Api_endpoint_returns_json_and_sets_owner_cookie()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/info", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var info = await response.Content.ReadFromJsonAsync<AppInfoResponse>(Ct);
        info.ShouldNotBeNull().Name.ShouldBe("FilmSearch");
        response.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith(OwnerCookie.Name + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_api_route_returns_problem_details()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
