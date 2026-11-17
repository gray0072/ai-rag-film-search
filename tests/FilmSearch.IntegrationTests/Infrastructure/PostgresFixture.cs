using FilmSearch.IntegrationTests.Infrastructure;
using Npgsql;
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

    /// <summary>Creates an empty database in the same container, for tests that must not share data.</summary>
    public async Task<string> CreateDatabaseAsync(string name)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
