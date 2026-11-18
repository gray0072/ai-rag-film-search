using System.Text.Json;
using FilmSearch.AI.Assistant;
using FilmSearch.AI.Assistant.Tools;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using Microsoft.Extensions.Caching.Memory;

namespace FilmSearch.UnitTests.Assistant;

public sealed class SearchMoviesToolTests
{
    private readonly FakeMovieSearch _search = new();
    private readonly SearchMoviesTool _tool;

    public SearchMoviesToolTests() =>
        _tool = new SearchMoviesTool(_search, new GenreCatalog(), new MemoryCache(new MemoryCacheOptions()), Microsoft.Extensions.Options.Options.Create(new AssistantOptions()));

    private static ToolContext Context => new(Guid.NewGuid(), "ru");

    [Fact]
    public async Task Arguments_become_a_hybrid_search_with_filters()
    {
        var outcome = await _tool.InvokeAsync(
            new Dictionary<string, object?>
            {
                ["query"] = "  космос  ",
                ["genresInclude"] = new List<string> { "Science-Fiction", "western" },
                ["genresExclude"] = new List<string> { "horror" },
                ["yearFrom"] = 1999,
                ["yearTo"] = 1990,
                ["minRating"] = "7.5",
                ["maxPrice"] = 4.99,
                ["language"] = "EN",
                ["limit"] = 50,
            },
            Context,
            TestContext.Current.CancellationToken);

        outcome.Ok.ShouldBeTrue();
        var query = _search.Queries.Single();
        query.Text.ShouldBe("космос");
        query.Mode.ShouldBe(SearchMode.Hybrid);
        query.Locale.ShouldBe("ru");
        query.PageSize.ShouldBe(SearchMoviesTool.MaxLimit);
        query.Understand.ShouldBeFalse();
        query.Filter.GenreSlugs.ShouldBe(["science-fiction"]);
        query.Filter.ExcludedGenreSlugs.ShouldBe(["horror"]);
        query.Filter.YearFrom.ShouldBe(1990);
        query.Filter.YearTo.ShouldBe(1999);
        query.Filter.MinRating.ShouldBe(7.5m);
        query.Filter.PriceMax.ShouldBe(499);
        query.Filter.Language.ShouldBe("en");
        outcome.Result.ShouldContain("Unknown genres ignored: western");
    }

    [Fact]
    public async Task Result_is_compact_and_cards_come_from_the_catalog_rows()
    {
        var movie = AssistantHarness.Movie("Солярис", 1972, overview: string.Join(' ', Enumerable.Repeat("слово", 100)));
        _search.Results = [movie];

        var outcome = await _tool.InvokeAsync(new Dictionary<string, object?> { ["query"] = "space" }, Context, TestContext.Current.CancellationToken);

        _search.Queries.Single().PageSize.ShouldBe(new AssistantOptions().SearchResultLimit);
        outcome.Movies.Single().Id.ShouldBe(movie.Id);
        outcome.Movies.Single().Offers.ShouldBe(movie.Offers);
        outcome.Result.ShouldContain("Солярис"); // not \u escaped: readable and fewer tokens

        using var json = JsonDocument.Parse(outcome.Result);
        var item = json.RootElement.GetProperty("movies")[0];
        item.GetProperty("id").GetGuid().ShouldBe(movie.Id);
        item.GetProperty("rating").GetDecimal().ShouldBe(7.8m);
        item.GetProperty("overview").GetString()!.Length.ShouldBeLessThanOrEqualTo(SearchMoviesTool.MaxOverviewLength);
        item.GetProperty("overview").GetString()!.ShouldEndWith("…");
        item.GetProperty("offers").EnumerateArray().Select(o => o.GetString()).ShouldBe(["buy: 9.99 USD", "rent for 48 h: 3.99 USD"]);
        json.RootElement.TryGetProperty("notes", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task No_results_tell_the_model_to_suggest_relaxing_the_request()
    {
        var outcome = await _tool.InvokeAsync(new Dictionary<string, object?> { ["query"] = "nothing" }, Context, TestContext.Current.CancellationToken);

        outcome.Ok.ShouldBeTrue();
        outcome.Movies.ShouldBeEmpty();
        outcome.Result.ShouldContain("No movies match");
    }

    [Fact]
    public async Task Malformed_arguments_are_an_error_result_not_an_exception()
    {
        var outcome = await _tool.InvokeAsync(new Dictionary<string, object?> { ["query"] = "space", ["yearFrom"] = "nineties" }, Context, TestContext.Current.CancellationToken);

        outcome.Ok.ShouldBeFalse();
        outcome.Result.ShouldContain("Invalid arguments for search_movies");
        _search.Queries.ShouldBeEmpty();
    }

    [Fact]
    public void Offer_prices_are_formatted_from_minor_units()
    {
        SearchMoviesTool.FormatOffer(new OfferItem(Guid.Empty, OfferType.Buy, 1299, "USD", null)).ShouldBe("buy: 12.99 USD");
        SearchMoviesTool.FormatOffer(new OfferItem(Guid.Empty, OfferType.Rent, 300, "USD", 72)).ShouldBe("rent for 72 h: 3.00 USD");
    }
}
