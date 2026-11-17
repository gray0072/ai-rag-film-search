using System.Diagnostics;
using System.Text.Json;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Ingestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FilmSearch.Infrastructure.Ingestion;

/// <summary>Configuration section <c>Catalog:Seed</c>.</summary>
public sealed class CatalogSeedOptions
{
    public const string SectionName = "Catalog:Seed";

    /// <summary>Seed file path. A relative path is resolved against the app's base directory (the API copies the file there on build).</summary>
    public string FilePath { get; set; } = "Seed/movies.seed.json";

    /// <summary>Load the seed file on startup when the catalog is empty (Development).</summary>
    public bool LoadOnStartup { get; set; }

    /// <summary>Movies per transaction.</summary>
    public int BatchSize { get; set; } = 100;

    public string ResolveFilePath() =>
        Path.IsPathRooted(FilePath) ? FilePath : Path.Combine(AppContext.BaseDirectory, FilePath);
}

internal sealed partial class CatalogSeeder(
    ICatalogReader reader,
    ICatalogWriter writer,
    IOptions<CatalogSeedOptions> options,
    ILogger<CatalogSeeder> logger) : ICatalogSeeder
{
    public async Task<SeedResult> LoadAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var path = options.Value.ResolveFilePath();
        var seed = await ReadAsync(path, cancellationToken);

        await writer.UpsertGenresAsync(seed.Genres, cancellationToken);

        var total = UpsertResult.Empty;
        foreach (var batch in seed.Movies.Chunk(options.Value.BatchSize))
        {
            total = total.Add(await writer.UpsertMoviesAsync(batch, MovieSource.Seed, cancellationToken));
        }

        var result = new SeedResult(seed.Genres.Count, total.Created, total.Updated, total.Unchanged, stopwatch.ElapsedMilliseconds);
        LogLoaded(logger, path, seed.Source, result.Created, result.Updated, result.Unchanged, result.ElapsedMilliseconds);
        return result;
    }

    public async Task<SeedResult?> LoadIfEmptyAsync(CancellationToken cancellationToken)
    {
        if (await reader.HasMoviesAsync(cancellationToken))
        {
            LogSkipped(logger);
            return null;
        }

        return await LoadAsync(cancellationToken);
    }

    private static async Task<SeedFile> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Seed file '{path}' was not found. See data/seed/README.md.", path);
        }

        await using var stream = File.OpenRead(path);
        var seed = await JsonSerializer.DeserializeAsync<SeedFile>(stream, SeedJson.Options, cancellationToken)
            ?? throw new InvalidDataException($"Seed file '{path}' is empty.");

        if (seed.Version != SeedFile.CurrentVersion)
        {
            throw new InvalidDataException(
                $"Seed file '{path}' has version {seed.Version}; this build reads version {SeedFile.CurrentVersion}.");
        }

        return seed;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seed file {Path} ({Source}) loaded: {Created} created, {Updated} updated, {Unchanged} unchanged in {ElapsedMs} ms")]
    private static partial void LogLoaded(ILogger logger, string path, string source, int created, int updated, int unchanged, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog already has movies; the seed file is not loaded on startup")]
    private static partial void LogSkipped(ILogger logger);
}
