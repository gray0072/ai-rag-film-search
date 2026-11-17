using NpgsqlTypes;

namespace FilmSearch.Infrastructure.Search.Keyword;

/// <summary>
/// Full-text document of one movie (spec 05 §2): the movie's texts flattened into one row, with English fallback
/// already applied, plus three generated <c>tsvector</c> columns (English, Russian and Swedish configs).
/// Written only by the SQL function <c>search.refresh_movie_documents</c> (see <see cref="MovieDocumentIndexer"/>);
/// EF Core only reads it.
/// </summary>
internal sealed class MovieSearchDocument
{
    public Guid MovieId { get; set; }

    public required string TitleEn { get; set; }

    public required string TitleRu { get; set; }

    public required string TitleSv { get; set; }

    public required string OriginalTitle { get; set; }

    /// <summary>TMDB keywords separated by spaces.</summary>
    public required string Keywords { get; set; }

    /// <summary>Names of directors, writers and cast separated by spaces.</summary>
    public required string People { get; set; }

    public string? TaglineEn { get; set; }

    public string? TaglineRu { get; set; }

    public string? TaglineSv { get; set; }

    public string? OverviewEn { get; set; }

    public string? OverviewRu { get; set; }

    public string? OverviewSv { get; set; }

    public NpgsqlTsVector SearchEn { get; set; } = null!;

    public NpgsqlTsVector SearchRu { get; set; } = null!;

    public NpgsqlTsVector SearchSv { get; set; } = null!;
}
