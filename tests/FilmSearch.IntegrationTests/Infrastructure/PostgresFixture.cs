using FilmSearch.IntegrationTests.Infrastructure;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL + pgvector container for the whole test assembly (same image as the AppHost).
/// Tests that change data must clean up after themselves or use their own database.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "pgvector/pgvector:pg17";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithDatabase("filmsearch")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
