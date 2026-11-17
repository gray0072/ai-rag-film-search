using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;

namespace FilmSearch.UnitTests.Search;

public sealed class UnderstoodFiltersTests
{
    private static readonly UnderstoodFilters Understood = new()
    {
        GenresInclude = ["science-fiction"],
        GenresExclude = ["horror"],
        YearFrom = 1990,
        YearTo = 1999,
        MinRating = 7,
        MaxPriceMinor = 500,
        Language = "en",
    };

    [Fact]
    public void Found_filters_fill_the_empty_request_filters()
    {
        var merged = Understood.MergeInto(MovieFilter.None);

        merged.GenreSlugs.ShouldBe(["science-fiction"]);
        merged.ExcludedGenreSlugs.ShouldBe(["horror"]);
        merged.YearFrom.ShouldBe(1990);
        merged.YearTo.ShouldBe(1999);
        merged.MinRating.ShouldBe(7);
        merged.PriceMax.ShouldBe(500);
        merged.Language.ShouldBe("en");
    }

    [Fact]
    public void Explicit_filters_win_and_excluded_genres_add_up()
    {
        var explicitFilter = new MovieFilter
        {
            GenreSlugs = ["drama"],
            ExcludedGenreSlugs = ["war"],
            YearFrom = 2000,
            MinRating = 8,
            RuntimeMax = 120,
            OfferType = OfferType.Rent,
        };

        var merged = Understood.MergeInto(explicitFilter);

        merged.GenreSlugs.ShouldBe(["drama"]);
        merged.ExcludedGenreSlugs.ShouldBe(["war", "horror"]);
        merged.YearFrom.ShouldBe(2000);
        merged.YearTo.ShouldBe(1999); // not set explicitly: taken from the text, even if it makes the result empty
        merged.MinRating.ShouldBe(8);
        merged.RuntimeMax.ShouldBe(120);
        merged.OfferType.ShouldBe(OfferType.Rent);
    }

    [Fact]
    public void Empty_understanding_changes_nothing()
    {
        var explicitFilter = new MovieFilter { YearFrom = 2000 };

        new UnderstoodFilters().MergeInto(explicitFilter).ShouldBe(explicitFilter with { ExcludedGenreSlugs = [] }, new MovieFilterComparer());
    }

    private sealed class MovieFilterComparer : IEqualityComparer<MovieFilter>
    {
        public bool Equals(MovieFilter? x, MovieFilter? y) =>
            x is not null && y is not null
            && x.GenreSlugs.SequenceEqual(y.GenreSlugs)
            && x.ExcludedGenreSlugs.SequenceEqual(y.ExcludedGenreSlugs)
            && x.YearFrom == y.YearFrom && x.YearTo == y.YearTo && x.MinRating == y.MinRating
            && x.PriceMax == y.PriceMax && x.Language == y.Language;

        public int GetHashCode(MovieFilter obj) => 0;
    }
}
