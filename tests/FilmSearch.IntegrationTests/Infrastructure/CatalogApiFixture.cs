using System.Net.Http.Json;
using System.Text.Json;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Ingestion;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>
/// The API with the committed seed loaded (through the admin endpoint) plus a few test-only movies.
/// The database is shared by the whole assembly, so tests assert on properties of results, not on global counts.
/// </summary>
public sealed class CatalogApiFixture(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>A test-only movie with English and Russian titles but no Swedish texts and no Russian overview.</summary>
    public const int FallbackMovieTmdbId = 990_001;

    public FilmSearchApiFactory Factory { get; } = new(postgres);

    public SeedFile Seed { get; private set; } = null!;

    public static JsonSerializerOptions Json => SeedJson.Options;

    public async ValueTask InitializeAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Seed", "movies.seed.json");
        await using (var stream = File.OpenRead(path))
        {
            Seed = (await JsonSerializer.DeserializeAsync<SeedFile>(stream, Json))!;
        }

        using var client = Factory.CreateClient();
        using var response = await client.PostAsync("/api/admin/ingestion/seed", content: null);
        response.EnsureSuccessStatusCode();

        await using var scope = Factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<ICatalogWriter>();
        await writer.UpsertMoviesAsync([FallbackMovie], MovieSource.Manual, CancellationToken.None);
    }

    public ValueTask DisposeAsync() => Factory.DisposeAsync();

    public HttpClient CreateClient(string? acceptLanguage = null)
    {
        var client = Factory.CreateClient();
        if (acceptLanguage is not null)
        {
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(acceptLanguage);
        }

        return client;
    }

    public async Task<Guid> FindMovieIdAsync(string englishTitle, CancellationToken cancellationToken)
    {
        using var client = CreateClient("en");
        var page = await client.GetFromJsonAsync<PagedResult<MovieSummary>>("/api/movies?sort=title&pageSize=50&page=1", Json, cancellationToken);
        for (var pageNumber = 1; page is not null; pageNumber++)
        {
            var movie = page.Items.FirstOrDefault(m => m.Title == englishTitle);
            if (movie is not null)
            {
                return movie.Id;
            }

            if (pageNumber * page.PageSize >= page.Total)
            {
                break;
            }

            page = await client.GetFromJsonAsync<PagedResult<MovieSummary>>(
                $"/api/movies?sort=title&pageSize=50&page={pageNumber + 1}", Json, cancellationToken);
        }

        throw new InvalidOperationException($"Movie '{englishTitle}' not found.");
    }

    private static MovieImport FallbackMovie => new()
    {
        TmdbId = FallbackMovieTmdbId,
        OriginalTitle = "Fallback Test Movie",
        OriginalLanguage = "en",
        ReleaseDate = new DateOnly(2005, 5, 5),
        RuntimeMinutes = 99,
        VoteAverage = 6.1m,
        VoteCount = 10,
        Popularity = 0.1,
        GenreIds = [35],
        Translations = new Dictionary<string, TranslationImport>
        {
            ["en"] = new("Fallback Test Movie", "English overview of the fallback movie.", "English tagline"),
            ["ru"] = new("Тестовый фильм", null, null),
        },
        Credits = [new CreditImport(null, "Test Director", CreditRole.Director, null, 0)],
    };
}
