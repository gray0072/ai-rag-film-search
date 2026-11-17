using FilmSearch.Core.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FilmSearch.Infrastructure.Persistence.Configurations.Catalog;

// EF Core mapping of the Catalog module. All tables live in the "catalog" schema (spec 02 §5: one schema per module).
// Enums are stored as strings: readable in SQL and safe when enum members are reordered.

internal static class CatalogSchema
{
    public const string Name = "catalog";
}

internal sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.ToTable("movies", CatalogSchema.Name);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.ImdbId).HasMaxLength(16);
        builder.Property(m => m.Title).HasMaxLength(500);
        builder.Property(m => m.OriginalTitle).HasMaxLength(500);
        builder.Property(m => m.OriginalLanguage).HasMaxLength(8);
        builder.Property(m => m.Tagline).HasMaxLength(1000);
        builder.Property(m => m.PosterPath).HasMaxLength(200);
        builder.Property(m => m.BackdropPath).HasMaxLength(200);
        builder.Property(m => m.VoteAverage).HasPrecision(3, 1);
        builder.Property(m => m.AgeRating).HasMaxLength(16);
        builder.Property(m => m.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.SourceHash).HasMaxLength(64);

        builder.HasIndex(m => m.TmdbId).IsUnique();
        builder.HasIndex(m => m.Year);
        builder.HasIndex(m => m.VoteAverage);
        builder.HasIndex(m => m.Popularity);
        builder.HasIndex(m => m.OriginalLanguage);

        builder.HasMany(m => m.Translations)
            .WithOne()
            .HasForeignKey(t => t.MovieId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Credits)
            .WithOne()
            .HasForeignKey(c => c.MovieId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Offers)
            .WithOne()
            .HasForeignKey(o => o.MovieId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Genres)
            .WithMany(g => g.Movies)
            .UsingEntity<Dictionary<string, object>>(
                "MovieGenre",
                right => right.HasOne<Genre>().WithMany().HasForeignKey("GenreId").OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne<Movie>().WithMany().HasForeignKey("MovieId").OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("movie_genres", CatalogSchema.Name);
                    join.HasKey("MovieId", "GenreId");
                    join.HasIndex("GenreId");
                });
    }
}

internal sealed class MovieTranslationConfiguration : IEntityTypeConfiguration<MovieTranslation>
{
    public void Configure(EntityTypeBuilder<MovieTranslation> builder)
    {
        builder.ToTable("movie_translations", CatalogSchema.Name);
        builder.HasKey(t => new { t.MovieId, t.Locale });
        builder.Property(t => t.Locale).HasMaxLength(8);
        builder.Property(t => t.Title).HasMaxLength(500);
        builder.Property(t => t.Tagline).HasMaxLength(1000);
    }
}

internal sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("genres", CatalogSchema.Name);
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Slug).HasMaxLength(64);
        builder.Property(g => g.NameEn).HasMaxLength(100);
        builder.Property(g => g.NameRu).HasMaxLength(100);
        builder.Property(g => g.NameSv).HasMaxLength(100);
        builder.HasIndex(g => g.Slug).IsUnique();
    }
}

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("people", CatalogSchema.Name);
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(300);
        builder.HasIndex(p => p.TmdbId).IsUnique();
        builder.HasIndex(p => p.Name);
    }
}

internal sealed class MovieCreditConfiguration : IEntityTypeConfiguration<MovieCredit>
{
    public void Configure(EntityTypeBuilder<MovieCredit> builder)
    {
        builder.ToTable("movie_credits", CatalogSchema.Name);
        builder.HasKey(c => new { c.MovieId, c.PersonId, c.Role });
        builder.Property(c => c.Role).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.Character).HasMaxLength(300);
        builder.HasOne(c => c.Person)
            .WithMany()
            .HasForeignKey(c => c.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.PersonId);
    }
}

internal sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("offers", CatalogSchema.Name);
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.Currency).HasMaxLength(3).IsFixedLength();
        builder.HasIndex(o => new { o.MovieId, o.Type }).IsUnique();
    }
}
