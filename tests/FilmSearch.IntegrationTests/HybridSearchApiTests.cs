using System.Net.Http.Json;
using FilmSearch.AI.Prompts;
using FilmSearch.Api.Modules.Search;
using FilmSearch.Core.Search;
using FilmSearch.IntegrationTests.Infrastructure;

namespace FilmSearch.IntegrationTests;

/// <summary>
/// Hybrid search (RRF of keyword and semantic lists) and query understanding over the committed seed (FR-SRCH-3,
/// FR-SRCH-4, FR-SRCH-6; spec 05 §4–5). Embeddings come from the fake bag-of-words model and chat answers from
/// <see cref="ScriptedChatClient"/>: these tests check the mechanics, not the quality of real models (I08 does that).
/// </summary>
public sealed class HybridSearchApiTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>, IAsyncLifetime
{
    private const int K = 20; // Search:Hybrid defaults
    private const double RelaxedKeywordWeight = 0.1;
    private static readonly string[] Crime = ["crime"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.EnsureEmbeddingsAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Hybrid_is_the_default_mode_and_scores_are_reciprocal_rank_fusion()
    {
        var result = await SearchAsync(new { query = "nolan dream", debug = true });

        result.Items[0].Movie.Title.ShouldBe("Inception");
        result.Degraded.ShouldBeFalse();
        result.UnderstoodQuery.ShouldBeNull();
        result.Items.Select(i => i.Score).ShouldBeInOrder(SortDirection.Descending);
        foreach (var item in result.Items)
        {
            var debug = item.Debug.ShouldNotBeNull();
            (debug.KeywordRank ?? debug.VectorRank).ShouldNotBeNull();
            var keywordWeight = debug.KeywordMatch == "all" ? 1.0 : RelaxedKeywordWeight;
            var expected = (debug.KeywordRank is { } kr ? keywordWeight / (K + kr) : 0) + (debug.VectorRank is { } vr ? 1.0 / (K + vr) : 0);
            debug.FusedScore.ShouldNotBeNull().ShouldBe(expected, 1e-6);
            item.Score.ShouldBe(debug.FusedScore.Value);
        }

        // Inception is found by both retrievers.
        var inception = result.Items[0].Debug!;
        inception.KeywordRank.ShouldNotBeNull();
        inception.VectorRank.ShouldNotBeNull();
        inception.Similarity.ShouldNotBeNull();
        inception.KeywordMatch.ShouldBe("all");
    }

    [Fact]
    public async Task Pages_continue_the_fused_ranking_and_total_is_the_fused_list()
    {
        var all = await SearchAsync(new { query = "gangster family", pageSize = 10 });
        var first = await SearchAsync(new { query = "gangster family", pageSize = 5, page = 1 });
        var second = await SearchAsync(new { query = "gangster family", pageSize = 5, page = 2 });

        first.Items.Concat(second.Items).Select(i => i.Movie.Id).ShouldBe(all.Items.Select(i => i.Movie.Id));
        all.Total.ShouldBeGreaterThanOrEqualTo(50); // the semantic list alone has 50 candidates
        all.Total.ShouldBeLessThanOrEqualTo(100); // union of two lists of 50
    }

    [Fact]
    public async Task Filters_are_applied_inside_both_retrievers()
    {
        var result = await SearchAsync(new { query = "gangster family", filters = new { yearFrom = 1980, genres = Crime, minRating = 7 }, pageSize = 50 });

        result.Items.ShouldNotBeEmpty();
        result.Items.ShouldAllBe(i => i.Movie.Year >= 1980 && i.Movie.VoteAverage >= 7 && i.Movie.Genres.Any(g => g.Slug == "crime"));
    }

    [Fact]
    public async Task Query_understanding_turns_the_text_into_filters_and_explicit_filters_win()
    {
        const string query = "космос девяностых, рейтинг от 7, без ужасов (hybrid test)";
        fixture.Factory.Chat.Script(query, """
            {"semanticQuery":"space astronauts","filters":{"genresInclude":[],"genresExclude":["horror"],"yearFrom":1990,
             "yearTo":1999,"minRating":7,"maxPriceMinor":null,"language":null},"similarToTitles":[],"detectedLocale":"ru"}
            """);

        var result = await SearchAsync(new { query, understand = true, filters = new { minRating = 7.5 }, pageSize = 50 });

        var understood = result.UnderstoodQuery.ShouldNotBeNull();
        understood.Source.ShouldBe(QueryUnderstandingSource.Llm);
        understood.PromptVersion.ShouldBe(new PromptLibrary().Get("query-understanding").Id);
        understood.SemanticQuery.ShouldBe("space astronauts");
        understood.DetectedLocale.ShouldBe("ru");

        result.AppliedFilters.ExcludeGenres.ShouldBe(["horror"]);
        result.AppliedFilters.YearFrom.ShouldBe(1990);
        result.AppliedFilters.YearTo.ShouldBe(1999);
        result.AppliedFilters.MinRating.ShouldBe(7.5m); // explicit filter wins over the 7 found in the text

        result.Items.ShouldNotBeEmpty();
        result.Items.ShouldAllBe(i =>
            i.Movie.Year >= 1990 && i.Movie.Year <= 1999 && i.Movie.VoteAverage >= 7.5m && i.Movie.Genres.All(g => g.Slug != "horror"));

        // The same query again (other case and spacing) comes from the cache: the model is called once.
        var again = await SearchAsync(new { query = "  " + query.ToUpperInvariant(), understand = true });
        again.UnderstoodQuery!.Source.ShouldBe(QueryUnderstandingSource.Cache);
        fixture.Factory.Chat.Requests.Count(r => r == query).ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_model_output_falls_back_to_the_plain_query()
    {
        const string query = "gangster family (unscripted, fallback test)";

        var result = await SearchAsync(new { query, understand = true, filters = new { genres = Crime } });

        var understood = result.UnderstoodQuery.ShouldNotBeNull();
        understood.Source.ShouldBe(QueryUnderstandingSource.Fallback);
        understood.SemanticQuery.ShouldBe(query);
        understood.Error.ShouldNotBeNullOrWhiteSpace();
        result.AppliedFilters.Genres.ShouldBe(Crime);
        result.AppliedFilters.ExcludeGenres.ShouldBeEmpty();
        result.Items.ShouldNotBeEmpty();

        // One try and one retry.
        fixture.Factory.Chat.Requests.Count(r => r == query).ShouldBe(2);
    }

    [Theory]
    [InlineData("AverageVector")]
    [InlineData("ExtraList")]
    public async Task Movies_like_a_title_find_its_neighbours_without_the_title_itself(string strategy)
    {
        var query = $"something like the godfather ({strategy})";
        fixture.Factory.Chat.Script(query, """
            {"semanticQuery":"","filters":{"genresInclude":[],"genresExclude":[],"yearFrom":null,"yearTo":null,
             "minRating":null,"maxPriceMinor":null,"language":null},"similarToTitles":["The Godfather"],"detectedLocale":"en"}
            """);
        var godfather = await fixture.FindMovieIdAsync("The Godfather", Ct);
        await using var factory = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("Search:Hybrid:SimilarTitles", strategy));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/search", new { query, understand = true, pageSize = 10 }, CatalogApiFixture.Json, Ct);
        var result = (await response.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SearchResponse>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull();

        result.UnderstoodQuery!.SimilarToTitles.ShouldBe(["The Godfather"]);
        result.Items.Select(i => i.Movie.Id).ShouldNotContain(godfather);
        result.Items.Select(i => i.Movie.Title).ShouldContain("The Godfather Part II");
    }

    [Fact]
    public async Task Understanding_works_in_keyword_and_semantic_modes_too()
    {
        const string query = "crime movies before 1980 (modes test)";
        fixture.Factory.Chat.Script(query, """
            {"semanticQuery":"gangster family","filters":{"genresInclude":["crime"],"genresExclude":[],"yearFrom":null,
             "yearTo":1979,"minRating":null,"maxPriceMinor":null,"language":null},"similarToTitles":[],"detectedLocale":"en"}
            """);

        foreach (var mode in new[] { "keyword", "semantic", "hybrid" })
        {
            var result = await SearchAsync(new { query, mode, understand = true });

            result.AppliedFilters.YearTo.ShouldBe(1979);
            result.Items.ShouldNotBeEmpty();
            result.Items.ShouldAllBe(i => i.Movie.Year <= 1979 && i.Movie.Genres.Any(g => g.Slug == "crime"));
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
}
