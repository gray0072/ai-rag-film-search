using System.Globalization;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;

namespace FilmSearch.AI.Assistant.Tools;

/// <summary>
/// Compact JSON shapes of tool results. The model gets only what it needs to choose, explain and act (ids, titles,
/// prices as text), not the full read models: fewer tokens, less room for confusion. Prices are formatted here from
/// the DB values, so the model can quote them but never computes them.
/// </summary>
internal static class ToolResults
{
    public const int MaxOverviewLength = 300;

    /// <summary>A movie in a list (search, similar): the overview is cut to keep the context small.</summary>
    public static object Movie(MovieSummary movie) => new
    {
        id = movie.Id,
        title = movie.Title,
        year = movie.Year,
        genres = movie.Genres.Select(g => g.Name),
        rating = Math.Round(movie.VoteAverage, 1),
        overview = Shorten(movie.Overview, MaxOverviewLength),
        offers = movie.Offers.Select(FormatOffer),
    };

    /// <summary>The cart as the model sees it: lines with the offer as text, and the total.</summary>
    public static object Cart(CartView cart) => new
    {
        items = cart.Items.Select(i => new
        {
            movieId = i.MovieId,
            title = i.Title,
            year = i.Year,
            offer = FormatOffer(new OfferItem(i.OfferId, i.OfferType, i.PriceMinor, i.Currency, i.RentalHours)),
            available = i.Available ? (bool?)null : false,
        }),
        total = FormatMoney(cart.TotalMinor, cart.Currency),
        notes = cart.Items.Any(i => !i.Available)
            ? "Lines with available: false are no longer sold; they block the checkout until they are removed."
            : null,
    };

    /// <summary><c>buy: 9.99 USD</c>, <c>rent for 48 h: 3.99 USD</c>.</summary>
    public static string FormatOffer(OfferItem offer)
    {
        var price = FormatMoney(offer.PriceMinor, offer.Currency);
        return offer.Type == OfferType.Rent && offer.RentalHours is { } hours
            ? $"rent for {hours.ToString(CultureInfo.InvariantCulture)} h: {price}"
            : $"{offer.Type.ToString().ToLowerInvariant()}: {price}";
    }

    /// <summary><c>9.99 USD</c> from minor units (all store currencies have two decimals).</summary>
    public static string FormatMoney(long minor, string currency) =>
        $"{(minor / 100m).ToString("0.00", CultureInfo.InvariantCulture)} {currency}";

    /// <summary>Cuts at a word boundary and adds an ellipsis.</summary>
    public static string? Shorten(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', maxLength - 1);
        return string.Concat(text.AsSpan(0, cut > maxLength / 2 ? cut : maxLength - 1), "…");
    }

    /// <summary>A refused commerce operation as a tool error the model can explain to the user.</summary>
    public static ToolOutcome CommerceError(CommerceException exception) => ToolOutcome.Error(exception.Error switch
    {
        Core.Commerce.CommerceError.MovieNotFound => $"{exception.Message} Use a movie id from a tool result.",
        Core.Commerce.CommerceError.OfferNotAvailable => $"{exception.Message} Check the offers of the movie and tell the user.",
        Core.Commerce.CommerceError.CartEmpty => "The cart is empty. Add a movie with add_to_cart first.",
        Core.Commerce.CommerceError.CartHasUnavailableItems => $"{exception.Message} Use view_cart and remove_from_cart.",
        _ => $"{exception.Message} Tell the user.",
    });
}
