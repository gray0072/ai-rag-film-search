using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FilmSearch.Api.Modules.Search;
using FilmSearch.Core.Catalog;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.IntegrationTests;

/// <summary>Keyword search over the committed seed (FR-SRCH-1, FR-SRCH-5, FR-SRCH-6; spec 05 §2).</summary>
public sealed class SearchApiTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>
{
    private static readonly string[] DramaOrCrime = ["drama", "crime"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Finds_a_movie_by_director_and_keyword()
    {
        // Acceptance criterion of I04: "nolan dream" finds Inception (director at weight B, keyword "dreams" stemmed).
        var result = await SearchAsync(new { query = "nolan dream", debug = true });

        result.Items.ShouldNotBeEmpty();
        result.Items[0].Movie.Title.ShouldBe("Inception");
        var debug = result.Items[0].Debug.ShouldNotBeNull();
        debug.KeywordRank.ShouldBe(1);
        debug.KeywordScore.ShouldNotBeNull().ShouldBeGreaterThan(0);
        debug.KeywordMatch.ShouldBe("all");
        debug.VectorRank.ShouldBeNull();
        result.Items[0].Score.ShouldBe(debug.KeywordScore.Value);
    }

    [Theory]
    [InlineData("крестный отец", "ru", "Крёстный отец")] // ё in the title, е in the query
    [InlineData("Крёстный отец", "ru", "Крёстный отец")]
    [InlineData("gudfadern", "sv", "Gudfadern")]
    [InlineData("the godfather", "en", "The Godfather")]
    [InlineData("побег из шоушенка", "en", "The Shawshank Redemption")] // query language differs from the UI locale
    [InlineData("nyckeln till frihet", "ru", "Побег из Шоушенка")]
    public async Task Finds_titles_in_english_russian_and_swedish(string query, string locale, string expectedTitle)
    {
        var result = await SearchAsync(new { query }, locale);

        // Top 3: sequels share the title words (and may mention them in their overviews too).
        result.Items.Take(3).Select(i => i.Movie.Title).ShouldContain(expectedTitle);
    }

    [Fact]
    public async Task Title_matches_rank_above_overview_matches()
    {
        // "godfather" is in the titles of three movies and only in the overviews of others: titles have weight A.
        var result = await SearchAsync(new { query = "godfather", pageSize = 5 });

        result.Items.Take(3).Select(i => i.Movie.Title).ShouldBe(
            ["The Godfather", "The Godfather Part II", "The Godfather Part III"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Filters_combine_with_the_query()
    {
        var unfiltered = await SearchAsync(new { query = "godfather", pageSize = 50 });
        var filtered = await SearchAsync(new
        {
            query = "godfather",
            pageSize = 50,
            filters = new { yearFrom = 1980, genres = DramaOrCrime, minRating = 7, offerType = "buy" },
        });

        unfiltered.Items.Select(i => i.Movie.Title).ShouldContain("The Godfather");
        filtered.Items.Select(i => i.Movie.Title).ShouldContain("The Godfather Part III");
        filtered.Items.Select(i => i.Movie.Title).ShouldNotContain("The Godfather");
        filtered.Total.ShouldBeLessThan(unfiltered.Total);
        filtered.Items.ShouldAllBe(i =>
            i.Movie.Year >= 1980
            && i.Movie.VoteAverage >= 7
            && i.Movie.Genres.Any(g => g.Slug == "drama" || g.Slug == "crime")
            && i.Movie.Offers.Any(o => o.Type == OfferType.Buy));

        filtered.AppliedFilters.YearFrom.ShouldBe(1980);
        filtered.AppliedFilters.Genres.ShouldBe(["drama", "crime"]);
        filtered.AppliedFilters.OfferType.ShouldBe(OfferType.Buy);
    }

    [Fact]
    public async Task Price_and_language_filters_apply_inside_the_search()
    {
        var result = await SearchAsync(new
        {
            query = "love",
            pageSize = 50,
            filters = new { language = "SV", priceMax = 999, offerType = "rent" },
        });

        result.Items.ShouldNotBeEmpty();
        var swedish = fixture.Seed.Movies.Where(m => m.OriginalLanguage == "sv").Select(m => m.OriginalTitle).ToHashSet();
        result.Items.ShouldAllBe(i => swedish.Contains(i.Movie.OriginalTitle));
        result.Items.ShouldAllBe(i => i.Movie.Offers.Any(o => o.Type == OfferType.Rent && o.PriceMinor <= 999));
        result.AppliedFilters.Language.ShouldBe("sv");
    }

    [Fact]
    public async Task Relaxes_the_query_to_any_word_when_nothing_matches_all_words()
    {
        var result = await SearchAsync(new { query = "nolan qwzxv", debug = true, pageSize = 50 });

        result.Items.ShouldNotBeEmpty();
        result.Items.ShouldAllBe(i => i.Debug!.KeywordMatch == "any");
        result.Items.Select(i => i.Movie.Title).ShouldContain("Inception");
    }

    [Fact]
    public async Task Returns_an_empty_page_when_nothing_matches()
    {
        var result = await SearchAsync(new { query = "qwzxv" });

        result.Items.ShouldBeEmpty();
        result.Total.ShouldBe(0);
    }

    [Fact]
    public async Task Pages_continue_the_ranking()
    {
        var first = await SearchAsync(new { query = "war", pageSize = 5, page = 1, debug = true });
        var second = await SearchAsync(new { query = "war", pageSize = 5, page = 2, debug = true });

        first.Total.ShouldBeGreaterThan(10);
        second.Total.ShouldBe(first.Total);
        first.Items.Select(i => i.Debug!.KeywordRank).ShouldBe([1, 2, 3, 4, 5]);
        second.Items.Select(i => i.Debug!.KeywordRank).ShouldBe([6, 7, 8, 9, 10]);
        first.Items.Select(i => i.Movie.Id).Intersect(second.Items.Select(i => i.Movie.Id)).ShouldBeEmpty();
        var scores = first.Items.Concat(second.Items).Select(i => i.Score).ToList();
        scores.ShouldBe(scores.OrderDescending().ToList());
        first.Items.ShouldAllBe(i => i.Debug != null);
    }

    [Fact]
    public async Task Debug_is_omitted_unless_requested()
    {
        var result = await SearchAsync(new { query = "godfather" });

        result.Items.ShouldNotBeEmpty();
        result.Items.ShouldAllBe(i => i.Debug == null);
    }

    [Fact]
    public async Task Search_documents_follow_catalog_changes()
    {
        var movie = new MovieImport
        {
            TmdbId = 990_300,
            OriginalTitle = "Zorbulax Rising",
            OriginalLanguage = "en",
            GenreIds = [878],
            Translations = new Dictionary<string, TranslationImport>
            {
                ["en"] = new("Zorbulax Rising", "A probe meets the quintessa nebula.", null),
                ["ru"] = new("Восход Зорбулакса", null, null),
            },
            Credits = [new CreditImport(990_301, "Vexilla Marrow", CreditRole.Director, null, 0)],
        };
        await UpsertAsync(movie);

        (await SearchAsync(new { query = "quintessa" })).Items.Select(i => i.Movie.Title).ShouldContain("Zorbulax Rising");
        (await SearchAsync(new { query = "vexilla marrow" })).Items.Select(i => i.Movie.Title).ShouldContain("Zorbulax Rising");
        (await SearchAsync(new { query = "зорбулакса" }, "ru")).Items.Select(i => i.Movie.Title).ShouldContain("Восход Зорбулакса");

        await UpsertAsync(movie with
        {
            Translations = new Dictionary<string, TranslationImport>
            {
                ["en"] = new("Zorbulax Rising", "A probe meets the brightwater nebula.", null),
            },
        });

        (await SearchAsync(new { query = "quintessa" })).Items.ShouldBeEmpty();
        (await SearchAsync(new { query = "brightwater" })).Items.Select(i => i.Movie.Title).ShouldContain("Zorbulax Rising");
    }

    [Theory]
    [InlineData("""{ "query": "" }""", "Query")]
    [InlineData("""{ "query": "godfather", "mode": "semantic" }""", "mode")]
    [InlineData("""{ "query": "godfather", "mode": "hybrid" }""", "mode")]
    [InlineData("""{ "query": "godfather", "understand": true }""", "understand")]
    [InlineData("""{ "query": "godfather", "pageSize": 51 }""", "PageSize")]
    [InlineData("""{ "query": "godfather", "filters": { "yearFrom": 2000, "yearTo": 1990 } }""", "YearFrom")]
    [InlineData("""{ "query": "godfather", "filters": { "minRating": 11 } }""", "MinRating")]
    public async Task Invalid_requests_return_validation_problems(string body, string field)
    {
        using var client = fixture.CreateClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/search", content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadAsStringAsync(Ct);
        problem.ShouldContain(field, Case.Insensitive);
    }

    [Theory]
    [InlineData("""{ "query": "godfather", "mode": "fuzzy" }""")]
    [InlineData("""{ "query": "godfather", "filters": { "offerType": "lease" } }""")]
    [InlineData("""not json""")]
    public async Task Malformed_bodies_return_400(string body)
    {
        using var client = fixture.CreateClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/search", content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Keyword_search_is_fast_on_the_seed()
    {
        // Acceptance criterion of I04: p95 < 200 ms locally. Asserted with a wide margin (CI machines vary);
        // the measured value goes to the test log.
        string[] queries =
        [
            "nolan dream", "крестный отец", "gudfadern", "war", "love story", "space", "the dark knight", "мафия",
            "kärlek", "prison escape", "tom hanks", "миядзаки", "samurai", "christmas", "robot", "vampire",
        ];
        using var client = fixture.CreateClient("en");
        foreach (var query in queries.Take(3))
        {
            await PostAsync(client, new { query }); // warm-up
        }

        var timings = new List<double>();
        for (var round = 0; round < 3; round++)
        {
            foreach (var query in queries)
            {
                var stopwatch = Stopwatch.StartNew();
                await PostAsync(client, new { query, filters = round == 1 ? new { minRating = 7 } : null });
                timings.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        timings.Sort();
        var p50 = timings[(int)(timings.Count * 0.50)];
        var p95 = timings[(int)Math.Ceiling(timings.Count * 0.95) - 1];
        TestContext.Current.TestOutputHelper?.WriteLine($"Keyword search over {fixture.Seed.Movies.Count} movies, {timings.Count} requests: p50 {p50:F0} ms, p95 {p95:F0} ms");
        p95.ShouldBeLessThan(1000);

        static async Task PostAsync(HttpClient client, object body)
        {
            using var response = await client.PostAsJsonAsync("/api/search", body, CatalogApiFixture.Json, Ct);
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task<SearchResponse> SearchAsync(object body, string locale = "en")
    {
        using var client = fixture.CreateClient(locale);
        using var response = await client.PostAsJsonAsync("/api/search", body, CatalogApiFixture.Json, Ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<SearchResponse>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull();
    }

    private async Task UpsertAsync(MovieImport movie)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<ICatalogWriter>();
        await writer.UpsertMoviesAsync([movie], MovieSource.Manual, Ct);
    }
}
