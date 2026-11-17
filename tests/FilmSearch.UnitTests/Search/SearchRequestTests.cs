using FilmSearch.Api.Modules.Search;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;

namespace FilmSearch.UnitTests.Search;

public sealed class SearchRequestTests
{
    [Fact]
    public void Defaults_to_keyword_mode_first_page_and_no_filters()
    {
        var query = new SearchRequest { Query = "  godfather  " }.ToQuery("sv");

        query.Text.ShouldBe("godfather");
        query.Mode.ShouldBe(SearchMode.Keyword);
        query.Locale.ShouldBe("sv");
        query.Page.ShouldBe(1);
        query.PageSize.ShouldBe(SearchRequest.DefaultPageSize);
        query.Debug.ShouldBeFalse();
        query.Filter.GenreSlugs.ShouldBeEmpty();
        query.Filter.ShouldBe(MovieFilter.None with { GenreSlugs = query.Filter.GenreSlugs });
    }

    [Fact]
    public void Normalizes_filters()
    {
        var filters = new SearchFilters
        {
            Genres = [" Drama", "crime", "DRAMA"],
            Language = " SV ",
            OfferType = OfferType.Rent,
            PriceMax = 399,
        };

        var filter = filters.ToFilter();

        filter.GenreSlugs.ShouldBe(["drama", "crime"]);
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
