using System.Net;
using System.Net.Http.Json;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Ingestion;
using FilmSearch.Infrastructure.Persistence;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.IntegrationTests;

public sealed class CatalogApiTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Filters_by_genre_year_range_and_rating()
    {
        // Acceptance criterion of I03 (FR-CAT-2).
        using var client = fixture.CreateClient("en");

        var page = await GetMoviesAsync(client, "/api/movies?genres=drama&yearFrom=1990&yearTo=2000&minRating=8&pageSize=50");

        var expected = fixture.Seed.Movies
            .Where(m => m.GenreIds.Contains(18)
                && m.ReleaseDate?.Year is >= 1990 and <= 2000
                && m.VoteAverage >= 8)
            .Select(m => m.Translations["en"].Title)
            .ToList();
        expected.ShouldNotBeEmpty();
        expected.ShouldContain("The Shawshank Redemption");
        page.Total.ShouldBe(expected.Count);
        page.Items.Select(m => m.Title).ShouldBe(expected, ignoreOrder: true);
        page.Items.ShouldAllBe(m => m.Year >= 1990 && m.Year <= 2000 && m.VoteAverage >= 8 && m.Genres.Any(g => g.Slug == "drama"));
    }

    [Fact]
    public async Task Genre_filter_matches_any_of_the_genres()
    {
        using var client = fixture.CreateClient();

        var page = await GetMoviesAsync(client, "/api/movies?genres=horror,animation&pageSize=50");

        page.Items.ShouldNotBeEmpty();
        page.Items.ShouldAllBe(m => m.Genres.Any(g => g.Slug == "horror" || g.Slug == "animation"));
        page.Items.ShouldContain(m => m.Genres.Any(g => g.Slug == "horror"));
        page.Items.ShouldContain(m => m.Genres.Any(g => g.Slug == "animation"));
    }

    [Fact]
    public async Task Filters_by_runtime_language_and_offer_price()
    {
        using var client = fixture.CreateClient();

        var swedish = await GetMoviesAsync(client, "/api/movies?language=sv&pageSize=50");
        var expectedSwedish = fixture.Seed.Movies.Where(m => m.OriginalLanguage == "sv").Select(m => m.OriginalTitle).ToList();
        expectedSwedish.ShouldContain("Det sjunde inseglet");
        swedish.Items.Select(m => m.OriginalTitle).ShouldBe(expectedSwedish, ignoreOrder: true);

        var shortMovies = await GetMoviesAsync(client, "/api/movies?runtimeMax=100&pageSize=50");
        shortMovies.Items.ShouldNotBeEmpty();
        shortMovies.Items.ShouldAllBe(m => m.RuntimeMinutes <= 100);

        var cheapRentals = await GetMoviesAsync(client, "/api/movies?offerType=rent&priceMax=299&pageSize=50");
        cheapRentals.Items.ShouldNotBeEmpty();
        cheapRentals.Items.ShouldAllBe(m => m.Offers.Any(o => o.Type == OfferType.Rent && o.PriceMinor <= 299));
    }

    [Fact]
    public async Task Pages_do_not_overlap_and_follow_the_sort_order()
    {
        using var client = fixture.CreateClient();

        var first = await GetMoviesAsync(client, "/api/movies?sort=rating&pageSize=5&page=1");
        var second = await GetMoviesAsync(client, "/api/movies?sort=rating&pageSize=5&page=2");

        first.Items.Count.ShouldBe(5);
        second.Items.Count.ShouldBe(5);
        first.Total.ShouldBe(second.Total);
        first.Items.Select(m => m.Id).Intersect(second.Items.Select(m => m.Id)).ShouldBeEmpty();
        var ratings = first.Items.Concat(second.Items).Select(m => m.VoteAverage).ToList();
        ratings.ShouldBe(ratings.OrderDescending().ToList());
    }

    [Theory]
    [InlineData("year")]
    [InlineData("Year")]
    public async Task Sorts_by_year_descending(string sort)
    {
        using var client = fixture.CreateClient();

        var page = await GetMoviesAsync(client, $"/api/movies?sort={sort}&pageSize=10");

        var years = page.Items.Select(m => m.Year ?? 0).ToList();
        years.ShouldBe(years.OrderDescending().ToList());
    }

    [Fact]
    public async Task Sorts_by_localized_title()
    {
        using var client = fixture.CreateClient("ru");

        // A narrow filter, so all matches fit on one page (and almost all of them have Russian titles).
        var page = await GetMoviesAsync(client, "/api/movies?sort=title&minRating=8.5&pageSize=50");
        page.Total.ShouldBeLessThanOrEqualTo(50);

        var titles = page.Items.Select(m => m.Title).ToList();
        titles.ShouldContain("Побег из Шоушенка");
        // Compare first letters only: the database collation and .NET differ on spaces and punctuation.
        var cyrillicInitials = titles.Select(t => char.ToUpperInvariant(t[0])).Where(c => c is >= 'А' and <= 'Я').ToList();
        cyrillicInitials.Count.ShouldBeGreaterThan(10);
        cyrillicInitials.ShouldBe(cyrillicInitials.Order().ToList());
    }

    [Theory]
    [InlineData("ru", "Побег из Шоушенка", "ru")]
    [InlineData("sv", "Nyckeln till frihet", "sv")]
    [InlineData("sv-SE,sv;q=0.9,en;q=0.8", "Nyckeln till frihet", "sv")]
    [InlineData("en", "The Shawshank Redemption", "en")]
    [InlineData("fr", "The Shawshank Redemption", "en")]
    public async Task Texts_follow_accept_language(string acceptLanguage, string expectedTitle, string expectedContentLanguage)
    {
        var id = await fixture.FindMovieIdAsync("The Shawshank Redemption", Ct);
        using var client = fixture.CreateClient(acceptLanguage);

        using var response = await client.GetAsync($"/api/movies/{id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentLanguage.ShouldContain(expectedContentLanguage);
        var movie = await response.Content.ReadFromJsonAsync<MovieDetails>(CatalogApiFixture.Json, Ct);
        movie.ShouldNotBeNull().Title.ShouldBe(expectedTitle);
    }

    [Fact]
    public async Task Missing_translation_falls_back_to_english()
    {
        var id = await fixture.FindMovieIdAsync("Fallback Test Movie", Ct);

        using var sv = fixture.CreateClient("sv");
        var swedish = await sv.GetFromJsonAsync<MovieDetails>($"/api/movies/{id}", CatalogApiFixture.Json, Ct);
        swedish.ShouldNotBeNull();
        swedish.Title.ShouldBe("Fallback Test Movie");
        swedish.Overview.ShouldBe("English overview of the fallback movie.");

        // Russian title exists, Russian overview and tagline do not: per-field fallback.
        using var ru = fixture.CreateClient("ru");
        var russian = await ru.GetFromJsonAsync<MovieDetails>($"/api/movies/{id}", CatalogApiFixture.Json, Ct);
        russian.ShouldNotBeNull();
        russian.Title.ShouldBe("Тестовый фильм");
        russian.Overview.ShouldBe("English overview of the fallback movie.");
        russian.Tagline.ShouldBe("English tagline");
    }

    [Fact]
    public async Task Movie_details_include_credits_genres_and_offers_from_pricing_rule()
    {
        var id = await fixture.FindMovieIdAsync("The Godfather", Ct);
        using var client = fixture.CreateClient("sv");

        var movie = await client.GetFromJsonAsync<MovieDetails>($"/api/movies/{id}", CatalogApiFixture.Json, Ct);

        movie.ShouldNotBeNull();
        movie.Title.ShouldBe("Gudfadern");
        movie.TmdbId.ShouldBe(238);
        movie.Year.ShouldBe(1972);
        movie.Directors.Select(d => d.Name).ShouldBe(["Francis Ford Coppola"]);
        movie.Cast[0].ShouldSatisfyAllConditions(
            c => c.Name.ShouldBe("Marlon Brando"),
            c => c.Character.ShouldBe("Don Vito Corleone"));
        movie.Genres.Select(g => g.Name).ShouldBe(["Kriminal", "Drama"], ignoreOrder: true);
        movie.Keywords.ShouldContain("mafia");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expected = PricingRule.Calculate(1972, movie.VoteCount, today);
        movie.Offers.Select(o => (o.Type, o.PriceMinor, o.Currency, o.RentalHours))
            .ShouldBe(expected.Select(p => (p.Type, p.PriceMinor, p.Currency, p.RentalHours)), ignoreOrder: true);
    }

    [Fact]
    public async Task Unknown_movie_returns_404_problem_details()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync($"/api/movies/{Guid.NewGuid()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Genres_are_localized()
    {
        using var client = fixture.CreateClient("sv");

        var genres = await client.GetFromJsonAsync<List<GenreItem>>("/api/genres", CatalogApiFixture.Json, Ct);

        genres.ShouldNotBeNull();
        genres.Count.ShouldBe(fixture.Seed.Genres.Count);
        genres.Single(g => g.Slug == "horror").Name.ShouldBe("Skräck");
        genres.Single(g => g.Slug == "adventure").Name.ShouldBe("Äventyr");
    }

    [Theory]
    [InlineData("pageSize=51")]
    [InlineData("page=0")]
    [InlineData("yearFrom=2001&yearTo=2000")]
    [InlineData("minRating=11")]
    [InlineData("sort=cheapest")]
    [InlineData("offerType=lease")]
    public async Task Invalid_query_returns_400_problem_details(string query)
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync($"/api/movies?{query}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Loading_the_seed_again_changes_nothing()
    {
        using var client = await fixture.Factory.CreateAdminClientAsync();

        using var response = await client.PostAsync("/api/admin/ingestion/seed", content: null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SeedResult>(CatalogApiFixture.Json, Ct);
        result.ShouldNotBeNull();
        result.Created.ShouldBe(0);
        result.Updated.ShouldBe(0);
        result.Unchanged.ShouldBe(fixture.Seed.Movies.Count);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seedIds = fixture.Seed.Movies.Select(m => (int?)m.TmdbId).ToList();
        (await db.Set<Movie>().CountAsync(m => seedIds.Contains(m.TmdbId), Ct)).ShouldBe(seedIds.Count);
        (await db.Set<Movie>().Where(m => seedIds.Contains(m.TmdbId)).SelectMany(m => m.Offers).CountAsync(Ct))
            .ShouldBe(seedIds.Count * 2);
    }

    [Fact]
    public async Task Concurrent_upserts_of_the_same_movies_do_not_duplicate()
    {
        MovieImport[] batch =
        [
            .. Enumerable.Range(990_100, 10).Select(id => new MovieImport
            {
                TmdbId = id,
                OriginalTitle = $"Concurrent {id}",
                OriginalLanguage = "en",
                GenreIds = [18],
                Translations = new Dictionary<string, TranslationImport> { ["en"] = new($"Concurrent {id}", null, null) },
                Credits = [new CreditImport(id, $"Person {id}", CreditRole.Cast, "Self", 0)],
            }),
        ];

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<ICatalogWriter>();
            return await writer.UpsertMoviesAsync(batch, MovieSource.Tmdb, Ct);
        }));

        results.Sum(r => r.Created).ShouldBe(batch.Length);
        results.Sum(r => r.Unchanged).ShouldBe(batch.Length * 3);

        await using var check = fixture.Factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Set<Movie>().CountAsync(m => m.TmdbId >= 990_100 && m.TmdbId < 990_110, Ct)).ShouldBe(batch.Length);
        (await db.Set<Person>().CountAsync(p => p.TmdbId >= 990_100 && p.TmdbId < 990_110, Ct)).ShouldBe(batch.Length);
    }

    [Fact]
    public async Task Changed_import_updates_the_movie()
    {
        var original = new MovieImport
        {
            TmdbId = 990_200,
            OriginalTitle = "Update Test",
            OriginalLanguage = "en",
            ReleaseDate = new DateOnly(1999, 1, 1),
            VoteCount = 100,
            GenreIds = [18, 35],
            Translations = new Dictionary<string, TranslationImport>
            {
                ["en"] = new("Update Test", "Old overview", null),
                ["sv"] = new("Uppdateringstest", null, null),
            },
            Credits = [new CreditImport(990_201, "Old Actor", CreditRole.Cast, "Hero", 0)],
        };
        var changed = original with
        {
            ReleaseDate = new DateOnly(2025, 1, 1),
            GenreIds = [35],
            Translations = new Dictionary<string, TranslationImport> { ["en"] = new("Update Test", "New overview", null) },
            Credits = [new CreditImport(990_202, "New Actor", CreditRole.Cast, "Hero", 0)],
        };

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<ICatalogWriter>();
            (await writer.UpsertMoviesAsync([original], MovieSource.Tmdb, Ct)).Created.ShouldBe(1);
            (await writer.UpsertMoviesAsync([changed], MovieSource.Tmdb, Ct)).Updated.ShouldBe(1);
        }

        await using var check = fixture.Factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var movie = await db.Set<Movie>()
            .Include(m => m.Genres)
            .Include(m => m.Translations)
            .Include(m => m.Credits).ThenInclude(c => c.Person)
            .Include(m => m.Offers)
            .AsSplitQuery()
            .SingleAsync(m => m.TmdbId == 990_200, Ct);

        movie.Year.ShouldBe(2025);
        movie.Overview.ShouldBe("New overview");
        movie.Genres.Select(g => g.Id).ShouldBe([35]);
        movie.Translations.Select(t => t.Locale).ShouldBe(["en"]);
        movie.Credits.Select(c => c.Person.Name).ShouldBe(["New Actor"]);
        movie.Offers.Single(o => o.Type == OfferType.Buy).PriceMinor
            .ShouldBe(PricingRule.Calculate(2025, 100, DateOnly.FromDateTime(DateTime.UtcNow))[0].PriceMinor);
    }

    private static async Task<PagedResult<MovieSummary>> GetMoviesAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PagedResult<MovieSummary>>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull();
    }
}
