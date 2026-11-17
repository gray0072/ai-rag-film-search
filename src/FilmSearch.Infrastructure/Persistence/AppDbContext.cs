using Microsoft.EntityFrameworkCore;

namespace FilmSearch.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context of the modular monolith. Each module keeps its tables in its own schema
/// and adds its entity configurations (<see cref="IEntityTypeConfiguration{TEntity}"/>) to this assembly.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // pgvector for embeddings (I06), pg_trgm for fuzzy title matching (I04).
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
