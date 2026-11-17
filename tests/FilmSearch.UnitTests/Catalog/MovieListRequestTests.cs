using System.ComponentModel.DataAnnotations;
using FilmSearch.Api.Modules.Catalog;
using FilmSearch.Core.Catalog;

namespace FilmSearch.UnitTests.Catalog;

public sealed class MovieListRequestTests
{
    [Fact]
    public void Defaults_are_first_page_of_20_sorted_by_popularity()
    {
        var query = new MovieListRequest().ToQuery();

        query.Page.ShouldBe(1);
        query.PageSize.ShouldBe(MovieListRequest.DefaultPageSize);
        query.Sort.ShouldBe(MovieSort.Popularity);
        query.Filter.GenreSlugs.ShouldBeEmpty();
        query.Filter.YearFrom.ShouldBeNull();
        query.Filter.Language.ShouldBeNull();
        query.Filter.OfferType.ShouldBeNull();
    }

    [Fact]
    public void Genre_csv_is_trimmed_lowercased_and_deduplicated()
    {
        var query = new MovieListRequest { Genres = " Drama, crime,,drama ,science-fiction" }.ToQuery();

        query.Filter.GenreSlugs.ShouldBe(["drama", "crime", "science-fiction"]);
    }

    [Fact]
    public void Year_and_runtime_ranges_must_be_ordered()
    {
        var request = new MovieListRequest { YearFrom = 2001, YearTo = 2000, RuntimeMin = 120, RuntimeMax = 90 };

        var errors = request.Validate(new ValidationContext(request)).ToList();

        errors.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("rating", MovieSort.Rating)]
    [InlineData("YEAR", MovieSort.Year)]
    [InlineData(" title ", MovieSort.Title)]
    public void Sort_is_parsed_case_insensitively(string value, MovieSort expected)
    {
        var request = new MovieListRequest { Sort = value, OfferType = "Rent" };

        request.Validate(new ValidationContext(request)).ShouldBeEmpty();
        request.ToQuery().Sort.ShouldBe(expected);
        request.ToQuery().Filter.OfferType.ShouldBe(OfferType.Rent);
    }

    [Theory]
    [InlineData("cheapest")]
    [InlineData("1")]
    [InlineData("")]
    public void Unknown_sort_is_rejected(string value)
    {
        var request = new MovieListRequest { Sort = value };

        request.Validate(new ValidationContext(request)).ShouldHaveSingleItem();
    }
}
