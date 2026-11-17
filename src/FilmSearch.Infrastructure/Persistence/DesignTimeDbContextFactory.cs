using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FilmSearch.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> to add migrations without starting the API.
/// The connection string is a placeholder: adding a migration does not connect to the database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseSetup.Configure(builder, "Host=localhost;Database=filmsearch");
        return new AppDbContext(builder.Options);
    }
}
