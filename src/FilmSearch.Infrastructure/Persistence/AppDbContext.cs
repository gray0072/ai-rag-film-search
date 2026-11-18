using FilmSearch.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FilmSearch.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context of the modular monolith. Each module keeps its tables in its own schema
/// and adds its entity configurations (<see cref="IEntityTypeConfiguration{TEntity}"/>) to this assembly.
/// It is also the ASP.NET Core Identity store (I17, ADR-0010): users and roles in the <c>identity</c> schema.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    /// <summary>
    /// Fixed Identity schema version (no passkeys table). By default Identity reads it from the app's
    /// <c>IdentityOptions</c>, which do not exist at design time: the migrations and the runtime model could differ.
    /// </summary>
    protected override Version SchemaVersion => IdentitySchemaVersions.Version2;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity tables first: the configurations below move them to the identity schema.
        base.OnModelCreating(builder);

        // pgvector for embeddings (I06), pg_trgm for fuzzy title matching (I04).
        builder.HasPostgresExtension("vector");
        builder.HasPostgresExtension("pg_trgm");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
