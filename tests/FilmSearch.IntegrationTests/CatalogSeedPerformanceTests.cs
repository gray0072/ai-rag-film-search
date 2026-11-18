using System.Diagnostics;
using FilmSearch.Core.Catalog;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.IntegrationTests;

/// <summary>
/// FR-DATA-1: about 1000 movies load in under a minute. Uses synthetic movies shaped like the TMDB seed
/// (3 locales, 10 cast members, keywords), in batches of 100 like the seed loader.
/// Runs in its own database, so the synthetic movies do not show up in other tests.
/// </summary>
public sealed class CatalogSeedPerformanceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private FilmSearchApiFactory _factory = null!;

    private const int FirstTmdbId = 2_000_000;
    private const int MovieCount = 1000;

    [Fact]
    public async Task Thousand_movies_load_in_under_a_minute_and_reload_unchanged()
    {
        var movies = Enumerable.Range(FirstTmdbId, MovieCount).Select(Movie).ToList();

        var stopwatch = Stopwatch.StartNew();
        var created = await UpsertAsync(movies);
        var firstLoad = stopwatch.Elapsed;

        stopwatch.Restart();
        var reloaded = await UpsertAsync(movies);
        var secondLoad = stopwatch.Elapsed;

        created.Created.ShouldBe(MovieCount);
        reloaded.Unchanged.ShouldBe(MovieCount);
        firstLoad.ShouldBeLessThan(TimeSpan.FromSeconds(60));
        secondLoad.ShouldBeLessThan(TimeSpan.FromSeconds(60));
        TestContext.Current.SendDiagnosticMessage($"1000 movies: first load {firstLoad.TotalSeconds:F1} s, reload {secondLoad.TotalSeconds:F1} s");
    }

    public async ValueTask InitializeAsync()
    {
        _factory = new FilmSearchApiFactory(postgres, await postgres.CreateDatabaseAsync("filmsearch_perf"));
        using var client = await _factory.CreateAdminClientAsync();
        (await client.PostAsync("/api/admin/ingestion/seed", content: null)).EnsureSuccessStatusCode();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private async Task<UpsertResult> UpsertAsync(List<MovieImport> movies)
    {
        var total = UpsertResult.Empty;
        foreach (var batch in movies.Chunk(100))
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<ICatalogWriter>();
            total = total.Add(await writer.UpsertMoviesAsync(batch, MovieSource.Tmdb, TestContext.Current.CancellationToken));
        }

        return total;
    }

    private static MovieImport Movie(int tmdbId) => new()
    {
        TmdbId = tmdbId,
        ImdbId = $"tt{tmdbId}",
        OriginalTitle = $"Synthetic {tmdbId}",
        OriginalLanguage = "en",
        ReleaseDate = new DateOnly(1950 + (tmdbId % 75), 1, 1),
        RuntimeMinutes = 90 + (tmdbId % 60),
        VoteAverage = 5 + (tmdbId % 50) / 10m,
        VoteCount = tmdbId % 20_000,
        Popularity = tmdbId % 100,
        GenreIds = [18, 35 + (tmdbId % 2) * 45],
        Keywords = ["synthetic", $"kw{tmdbId % 97}", $"kw{tmdbId % 89}"],
        Translations = new Dictionary<string, TranslationImport>
        {
            ["en"] = new($"Synthetic {tmdbId}", new string('e', 400), "Tagline"),
            ["ru"] = new($"Синтетический {tmdbId}", new string('р', 400), null),
            ["sv"] = new($"Syntetisk {tmdbId}", new string('s', 400), null),
        },
        Credits =
        [
            new CreditImport(3_000_000 + (tmdbId % 300), $"Director {tmdbId % 300}", CreditRole.Director, null, 0),
            .. Enumerable.Range(0, 10).Select(i =>
                new CreditImport(4_000_000 + ((tmdbId * 7 + i) % 3000), $"Actor {(tmdbId * 7 + i) % 3000}", CreditRole.Cast, $"Role {i}", i)),
        ],
    };
}
