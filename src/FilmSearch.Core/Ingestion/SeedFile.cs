using FilmSearch.Core.Catalog;

namespace FilmSearch.Core.Ingestion;

/// <summary>
/// Root of <c>data/seed/movies.seed.json</c>. Written by <c>tools/seed-builder</c>, read by the seed loader.
/// Format: <c>data/seed/README.md</c>.
/// </summary>
public sealed record SeedFile
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary><c>tmdb</c> (built by the seed builder) or <c>hand-made</c>.</summary>
    public required string Source { get; init; }

    public DateTimeOffset GeneratedAt { get; init; }

    public IReadOnlyList<GenreImport> Genres { get; init; } = [];

    public IReadOnlyList<MovieImport> Movies { get; init; } = [];
}
