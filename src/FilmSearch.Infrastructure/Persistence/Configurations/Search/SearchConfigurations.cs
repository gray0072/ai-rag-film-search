using FilmSearch.Core.Catalog;
using FilmSearch.Infrastructure.Search.Keyword;
using FilmSearch.Infrastructure.Search.Semantic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FilmSearch.Infrastructure.Persistence.Configurations.Search;

// EF Core mapping of the Search module: tables in the "search" schema.

internal static class SearchSchema
{
    public const string Name = "search";
}

internal sealed class MovieSearchDocumentConfiguration : IEntityTypeConfiguration<MovieSearchDocument>
{
    public void Configure(EntityTypeBuilder<MovieSearchDocument> builder)
    {
        builder.ToTable("movie_documents", SearchSchema.Name);
        builder.HasKey(d => d.MovieId);

        // Deleting a movie deletes its document. No navigation: the Search module reads catalog tables only in queries.
        builder.HasOne<Movie>()
            .WithOne()
            .HasForeignKey<MovieSearchDocument>(d => d.MovieId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(d => d.Keywords).HasDefaultValue(string.Empty);
        builder.Property(d => d.People).HasDefaultValue(string.Empty);

        builder.Property(d => d.SearchEn).HasComputedColumnSql(TsVectorSql("english", "en"), stored: true);
        builder.Property(d => d.SearchRu).HasComputedColumnSql(TsVectorSql("russian", "ru"), stored: true);
        builder.Property(d => d.SearchSv).HasComputedColumnSql(TsVectorSql("swedish", "sv"), stored: true);

        builder.HasIndex(d => d.SearchEn).HasMethod("gin");
        builder.HasIndex(d => d.SearchRu).HasMethod("gin");
        builder.HasIndex(d => d.SearchSv).HasMethod("gin");
    }

    /// <summary>
    /// Weighted <c>tsvector</c> of one locale (spec 05 §2): titles A, keywords and people B, tagline C, overview D.
    /// <c>ё</c> is folded to <c>е</c> (Russian texts use both spellings; the query is folded the same way).
    /// </summary>
    private static string TsVectorSql(string config, string locale)
    {
        string Part(string text, char weight) =>
            $"setweight(to_tsvector('{config}'::regconfig, translate({text}, 'Ёё', 'Ее')), '{weight}')";

        return string.Join(
            " || ",
            Part($"title_{locale} || ' ' || original_title", 'A'),
            Part("keywords || ' ' || people", 'B'),
            Part($"coalesce(tagline_{locale}, '')", 'C'),
            Part($"coalesce(overview_{locale}, '')", 'D'));
    }
}

internal sealed class MovieEmbeddingConfiguration : IEntityTypeConfiguration<MovieEmbedding>
{
    public void Configure(EntityTypeBuilder<MovieEmbedding> builder)
    {
        builder.ToTable("movie_embeddings", SearchSchema.Name);
        builder.HasKey(e => e.Id);

        // Deleting a movie deletes its vectors. No navigation, as for the search documents.
        builder.HasOne<Movie>()
            .WithMany()
            .HasForeignKey(e => e.MovieId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.Model).HasMaxLength(200);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Locale).HasMaxLength(8);
        builder.Property(e => e.ContentHash).HasMaxLength(64);

        // No dimension in the column type (ADR-0008): vectors of several models live side by side. The HNSW indexes are
        // partial expression indexes per dimension (VectorIndex), created in SQL: EF Core cannot model them.
        builder.Property(e => e.Vector).HasColumnType("vector");

        builder.HasIndex(e => new { e.MovieId, e.Model, e.Kind, e.Locale }).IsUnique();
        builder.HasIndex(e => new { e.Model, e.Locale });
    }
}
