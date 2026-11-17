namespace FilmSearch.Core.Ingestion;

/// <summary>Loads the committed seed file into the catalog (FR-DATA-1).</summary>
public interface ICatalogSeeder
{
    /// <summary>Loads the whole seed file. Idempotent: a second run reports every movie as unchanged.</summary>
    Task<SeedResult> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Loads the seed file only when the catalog has no movies. Returns null when it was skipped.</summary>
    Task<SeedResult?> LoadIfEmptyAsync(CancellationToken cancellationToken);
}

public sealed record SeedResult(int Genres, int Created, int Updated, int Unchanged, long ElapsedMilliseconds);
