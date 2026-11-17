using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace FilmSearch.AiPlayground;

/// <summary>
/// Fake versions of the assistant tools of spec 06 §3 with a tiny in-memory catalog. The JSON schema the model sees is
/// generated from the method signatures and [Description] attributes by <see cref="AIFunctionFactory"/>.
/// </summary>
internal static class FakeCatalogTools
{
    private static readonly FakeMovie[] Movies =
    [
        new(1, "The Matrix", 1999, ["science fiction", "action"], 8.2m, 3.99m, 1.99m),
        new(2, "Inception", 2010, ["science fiction", "thriller"], 8.4m, 4.99m, 2.49m),
        new(3, "The Godfather", 1972, ["crime", "drama"], 8.7m, 4.99m, 2.99m),
        new(4, "Toy Story", 1995, ["animation", "family", "comedy"], 8.0m, 2.99m, 1.49m),
        new(5, "Alien", 1979, ["horror", "science fiction"], 8.1m, 3.49m, 1.99m),
        new(8, "Interstellar", 2014, ["science fiction", "drama"], 8.5m, 4.99m, 2.49m),
        new(17, "The Grand Budapest Hotel", 2014, ["comedy", "drama"], 8.0m, 3.99m, 1.99m),
        new(42, "Spirited Away", 2001, ["animation", "fantasy", "family"], 8.5m, 3.99m, 1.99m),
    ];

    private static readonly List<CartLine> Cart = [];

    public static IList<AITool> All { get; } =
    [
        AIFunctionFactory.Create(SearchMovies, "search_movies"),
        AIFunctionFactory.Create(GetMovieDetails, "get_movie_details"),
        AIFunctionFactory.Create(ViewCart, "view_cart"),
        AIFunctionFactory.Create(AddToCart, "add_to_cart"),
        AIFunctionFactory.Create(RemoveFromCart, "remove_from_cart"),
    ];

    [Description("Searches the movie catalog by free text and filters. Returns at most 5 movies with ids and prices.")]
    private static IEnumerable<object> SearchMovies(
        [Description("What the user is looking for, e.g. 'mind-bending heist'")] string query,
        [Description("Genres in English, lower case, e.g. 'comedy', 'science fiction'")] string[]? genres = null,
        [Description("Earliest release year")] int? yearFrom = null,
        [Description("Latest release year")] int? yearTo = null,
        [Description("Maximum price to buy, USD")] decimal? maxPrice = null) =>
        Movies
            .Where(m => genres is not { Length: > 0 } || m.Genres.Intersect(genres, StringComparer.OrdinalIgnoreCase).Any())
            .Where(m => yearFrom is null || m.Year >= yearFrom)
            .Where(m => yearTo is null || m.Year <= yearTo)
            .Where(m => maxPrice is null || m.BuyPrice <= maxPrice)
            .Take(5)
            .Select(m => new { m.Id, m.Title, m.Year, m.Genres, m.Rating, buyUsd = m.BuyPrice, rentUsd = m.RentPrice });

    [Description("Returns full details of one movie by id.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "The tool returns a movie or an error object.")]
    private static object GetMovieDetails([Description("Movie id")] int movieId) =>
        Movies.FirstOrDefault(m => m.Id == movieId) is { } movie
            ? movie
            : new { error = $"Movie {movieId} not found." };

    [Description("Returns the items in the user's cart and the total.")]
    private static object ViewCart() => new { items = Cart, totalUsd = Cart.Sum(l => l.PriceUsd) };

    [Description("Adds a movie to the user's cart, to buy or to rent.")]
    private static object AddToCart(
        [Description("Movie id")] int movieId,
        [Description("'buy' or 'rent'")] string offerType)
    {
        if (Movies.FirstOrDefault(m => m.Id == movieId) is not { } movie)
        {
            return new { error = $"Movie {movieId} not found." };
        }

        var rent = string.Equals(offerType, "rent", StringComparison.OrdinalIgnoreCase);
        Cart.RemoveAll(l => l.MovieId == movieId);
        Cart.Add(new CartLine(movieId, movie.Title, rent ? "rent" : "buy", rent ? movie.RentPrice : movie.BuyPrice));
        return ViewCart();
    }

    [Description("Removes a movie from the user's cart.")]
    private static object RemoveFromCart([Description("Movie id")] int movieId)
    {
        Cart.RemoveAll(l => l.MovieId == movieId);
        return ViewCart();
    }

    private sealed record FakeMovie(int Id, string Title, int Year, string[] Genres, decimal Rating, decimal BuyPrice, decimal RentPrice);

    private sealed record CartLine(int MovieId, string Title, string OfferType, decimal PriceUsd);
}

/// <summary>
/// Search request extracted from a user message with structured output (a preview of query understanding, I07).
/// Language and genres are enums: the JSON Schema then lists the allowed values and LM Studio constrains generation to
/// them. Descriptions alone ("in English", "en, ru or sv") are often ignored by small models.
/// </summary>
[Description("Search request extracted from a message of a movie store customer.")]
internal sealed record SearchIntent(
    [property: Description("What the movie should be about, in English, without the filters")] string Query,
    [property: Description("Genres of the catalog that match the request; empty when none is asked for")] IntentGenre[] Genres,
    [property: Description("Earliest release year, null when not given")] int? YearFrom,
    [property: Description("Latest release year, null when not given")] int? YearTo,
    [property: Description("Maximum price in USD, null when not given")] decimal? MaxPriceUsd,
    [property: Description("Language of the message")] IntentLanguage Language);

[JsonConverter(typeof(JsonStringEnumConverter<IntentLanguage>))]
internal enum IntentLanguage
{
    [JsonStringEnumMemberName("en")] En,
    [JsonStringEnumMemberName("ru")] Ru,
    [JsonStringEnumMemberName("sv")] Sv,
}

/// <summary>Genre slugs of the catalog (data/seed).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IntentGenre>))]
internal enum IntentGenre
{
    [JsonStringEnumMemberName("action")] Action,
    [JsonStringEnumMemberName("adventure")] Adventure,
    [JsonStringEnumMemberName("animation")] Animation,
    [JsonStringEnumMemberName("comedy")] Comedy,
    [JsonStringEnumMemberName("crime")] Crime,
    [JsonStringEnumMemberName("documentary")] Documentary,
    [JsonStringEnumMemberName("drama")] Drama,
    [JsonStringEnumMemberName("family")] Family,
    [JsonStringEnumMemberName("fantasy")] Fantasy,
    [JsonStringEnumMemberName("history")] History,
    [JsonStringEnumMemberName("horror")] Horror,
    [JsonStringEnumMemberName("music")] Music,
    [JsonStringEnumMemberName("mystery")] Mystery,
    [JsonStringEnumMemberName("romance")] Romance,
    [JsonStringEnumMemberName("science-fiction")] ScienceFiction,
    [JsonStringEnumMemberName("thriller")] Thriller,
    [JsonStringEnumMemberName("tv-movie")] TvMovie,
    [JsonStringEnumMemberName("war")] War,
    [JsonStringEnumMemberName("western")] Western,
}
