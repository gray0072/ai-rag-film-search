using FilmSearch.Api.Modules.Search;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;

namespace FilmSearch.UnitTests.Search;

public sealed class SearchRequestTests
{
    [Fact]
    public void Defaults_to_the_configured_mode_first_page_no_filters_and_no_understanding()
    {
        var query = new SearchRequest { Query = "  godfather  " }.ToQuery("sv", SearchMode.Semantic);

        query.Text.ShouldBe("godfather");
        query.Mode.ShouldBe(SearchMode.Semantic);
        query.Understand.ShouldBeFalse();
        query.Locale.ShouldBe("sv");
        query.Page.ShouldBe(1);
        query.PageSize.ShouldBe(SearchRequest.DefaultPageSize);
        query.Debug.ShouldBeFalse();
        query.Filter.GenreSlugs.ShouldBeEmpty();
        query.Filter.ShouldBe(MovieFilter.None with { GenreSlugs = query.Filter.GenreSlugs });
    }

    [Fact]
    public void A_mode_in_the_request_wins_over_the_default()
    {
        new SearchRequest { Query = "godfather", Mode = SearchMode.Keyword }.ToQuery("en", SearchMode.Hybrid)
            .Mode.ShouldBe(SearchMode.Keyword);
    }

    [Fact]
    public void Default_search_settings_are_hybrid_with_query_understanding()
    {
        var defaults = new SearchDefaultsOptions();

        defaults.Mode.ShouldBe(SearchMode.Hybrid);
        defaults.Understand.ShouldBeTrue();
    }

    [Fact]
    public void Normalizes_filters()
    {
        var filters = new SearchFilters
        {
            Genres = [" Drama", "crime", "DRAMA"],
            ExcludeGenres = ["Horror "],
            Language = " SV ",
            OfferType = OfferType.Rent,
            PriceMax = 399,
        };

        var filter = filters.ToFilter();

        filter.GenreSlugs.ShouldBe(["drama", "crime"]);
        filter.ExcludedGenreSlugs.ShouldBe(["horror"]);
        filter.Language.ShouldBe("sv");
        filter.OfferType.ShouldBe(OfferType.Rent);
        filter.PriceMax.ShouldBe(399);
        SearchFilters.From(filter).Genres.ShouldBe(["drama", "crime"]);
    }

    [Theory]
    [InlineData(2000, 1990, null, null, "YearFrom")]
    [InlineData(null, null, 120, 90, "RuntimeMin")]
    public void Rejects_inverted_ranges(int? yearFrom, int? yearTo, int? runtimeMin, int? runtimeMax, string member)
    {
        var filters = new SearchFilters { YearFrom = yearFrom, YearTo = yearTo, RuntimeMin = runtimeMin, RuntimeMax = runtimeMax };

        var errors = filters.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(filters)).ToList();

        errors.ShouldHaveSingleItem().MemberNames.ShouldBe([member]);
    }

    [Fact]
    public void Rejects_blank_genre_slugs()
    {
        var filters = new SearchFilters { Genres = ["drama", " "] };

        filters.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(filters)).ShouldNotBeEmpty();
    }
}
