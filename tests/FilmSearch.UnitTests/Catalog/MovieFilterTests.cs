using FilmSearch.Core.Catalog;

namespace FilmSearch.UnitTests.Catalog;

public sealed class MovieFilterTests
{
    public static TheoryData<MovieFilter> FiltersWithOneCondition =>
    [
        new MovieFilter { GenreSlugs = ["drama"] },
        new MovieFilter { YearFrom = 1990 },
        new MovieFilter { YearTo = 1990 },
        new MovieFilter { MinRating = 7 },
        new MovieFilter { RuntimeMin = 60 },
        new MovieFilter { RuntimeMax = 120 },
        new MovieFilter { Language = "sv" },
        new MovieFilter { PriceMax = 499 },
        new MovieFilter { OfferType = OfferType.Rent },
    ];

    [Fact]
    public void Empty_filter_has_no_conditions()
    {
        MovieFilter.None.HasConditions().ShouldBeFalse();
        new MovieFilter { Language = " " }.HasConditions().ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(FiltersWithOneCondition))]
    public void Any_set_condition_counts(MovieFilter filter) => filter.HasConditions().ShouldBeTrue();
}
