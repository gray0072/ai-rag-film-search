using FilmSearch.Core.Catalog;

namespace FilmSearch.UnitTests.Catalog;

public sealed class PricingRuleTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    [Theory]
    [InlineData(2026, 1499, 499)]
    [InlineData(2024, 1499, 499)]
    [InlineData(2023, 1199, 399)]
    [InlineData(2016, 1199, 399)]
    [InlineData(2015, 999, 299)]
    [InlineData(2001, 999, 299)]
    [InlineData(2000, 799, 299)]
    [InlineData(1957, 799, 299)]
    public void Price_depends_on_movie_age(int year, long buy, long rent)
    {
        var prices = PricingRule.Calculate(year, voteCount: 100, Today);

        Price(prices, OfferType.Buy).ShouldBe(buy);
        Price(prices, OfferType.Rent).ShouldBe(rent);
    }

    [Fact]
    public void Widely_watched_movies_cost_more()
    {
        var regular = PricingRule.Calculate(1994, PricingRule.PopularVoteCount - 1, Today);
        var popular = PricingRule.Calculate(1994, PricingRule.PopularVoteCount, Today);

        Price(popular, OfferType.Buy).ShouldBe(Price(regular, OfferType.Buy) + 200);
        Price(popular, OfferType.Rent).ShouldBe(Price(regular, OfferType.Rent) + 100);
    }

    [Fact]
    public void Unknown_year_is_priced_as_a_classic()
    {
        var prices = PricingRule.Calculate(year: null, voteCount: 0, Today);

        Price(prices, OfferType.Buy).ShouldBe(799);
        Price(prices, OfferType.Rent).ShouldBe(299);
    }

    [Fact]
    public void Future_release_is_priced_as_new()
    {
        Price(PricingRule.Calculate(2027, 0, Today), OfferType.Buy).ShouldBe(1499);
    }

    [Fact]
    public void Returns_buy_and_rent_offers_in_usd_with_48_hour_rental()
    {
        var prices = PricingRule.Calculate(2010, 5000, Today);

        prices.Select(p => p.Type).ShouldBe([OfferType.Buy, OfferType.Rent]);
        prices.ShouldAllBe(p => p.Currency == "USD");
        prices.Single(p => p.Type == OfferType.Buy).RentalHours.ShouldBeNull();
        prices.Single(p => p.Type == OfferType.Rent).RentalHours.ShouldBe(48);
    }

    [Fact]
    public void Rent_is_always_cheaper_than_buy()
    {
        for (var year = 1900; year <= 2030; year++)
        {
            foreach (var votes in new[] { 0, PricingRule.PopularVoteCount })
            {
                var prices = PricingRule.Calculate(year, votes, Today);
                Price(prices, OfferType.Rent).ShouldBeLessThan(Price(prices, OfferType.Buy));
            }
        }
    }

    private static long Price(IReadOnlyList<OfferPrice> prices, OfferType type) =>
        prices.Single(p => p.Type == type).PriceMinor;
}
