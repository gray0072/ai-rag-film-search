namespace FilmSearch.Core.Catalog;

/// <summary>A price computed by <see cref="PricingRule"/>.</summary>
public sealed record OfferPrice(OfferType Type, long PriceMinor, string Currency, int? RentalHours);

/// <summary>
/// Synthetic prices (spec 08 §2: computed at load time, not stored in the seed file).
/// Newer movies cost more; widely watched ones get a small premium.
/// "Widely watched" uses the vote count, not TMDB <c>popularity</c>: popularity changes every day,
/// and prices should not move with every import.
/// </summary>
public static class PricingRule
{
    public const string Currency = "USD";
    public const int RentalHours = 48;

    /// <summary>Movies with at least this many votes get the premium.</summary>
    public const int PopularVoteCount = 10_000;

    public static IReadOnlyList<OfferPrice> Calculate(int? year, int voteCount, DateOnly today)
    {
        // Unknown year: priced as a catalog classic.
        var age = year is { } y ? Math.Max(0, today.Year - y) : int.MaxValue;
        var popular = voteCount >= PopularVoteCount;

        long buy = age switch
        {
            <= 2 => 1499,
            <= 10 => 1199,
            <= 25 => 999,
            _ => 799,
        };

        long rent = age switch
        {
            <= 2 => 499,
            <= 10 => 399,
            _ => 299,
        };

        if (popular)
        {
            buy += 200;
            rent += 100;
        }

        return
        [
            new OfferPrice(OfferType.Buy, buy, Currency, RentalHours: null),
            new OfferPrice(OfferType.Rent, rent, Currency, RentalHours),
        ];
    }
}
