using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Localization;
using FilmSearch.Core.Search;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FilmSearch.AI.Assistant.Tools;

/// <summary>
/// Arguments of <c>search_movies</c>. Filters are flat top-level arguments (spec 06 §3 groups them as <c>filters</c>):
/// small local models fill flat arguments more reliably than nested objects.
/// </summary>
internal sealed record SearchMoviesArgs(
    [property: Description("What the movie should be about: topic, mood, plot, setting, people. A few words, in English or in the user's language.")]
    string Query,
    [property: Description("Genre slugs the movie should have (any of them). Only when the user asks for a genre.")]
    IReadOnlyList<string>? GenresInclude = null,
    [property: Description("Genre slugs the movie must not have.")]
    IReadOnlyList<string>? GenresExclude = null,
    [property: Description("First release year, inclusive.")]
    int? YearFrom = null,
    [property: Description("Last release year, inclusive.")]
    int? YearTo = null,
    [property: Description("Minimum user rating, 0-10.")]
    decimal? MinRating = null,
    [property: Description("Maximum price in USD, e.g. 4.99.")]
    decimal? MaxPrice = null,
    [property: Description("ISO 639-1 code of the original language, e.g. fr. Only when the user asks for it.")]
    string? Language = null,
    [property: Description("How many movies to return, 1-10. Default 6.")]
    int? Limit = null,
    [property: Description("Ids of movies to leave out, e.g. ones already recommended.")]
    IReadOnlyList<Guid>? ExcludeMovieIds = null);

/// <summary>
/// <c>search_movies</c> (spec 06 §3): hybrid search (keyword + semantic, I07) with the filters from the arguments. This is
/// the retrieval step of RAG: the model gets a compact list of real catalog movies (id, title, year, genres, rating, a
/// short overview, prices) and may recommend only those; the UI gets cards built from the same catalog rows.
/// </summary>
internal sealed class SearchMoviesTool(
    IMovieSearch search,
    ICatalogReader catalog,
    IMemoryCache cache,
    IOptions<AssistantOptions> options) : AssistantTool<SearchMoviesArgs>
{
    public const string ToolName = "search_movies";

    public const int MaxLimit = 10;
    public const int MaxQueryLength = 500;
    public const int MaxOverviewLength = 300;

    private static readonly TimeSpan GenreCacheTime = TimeSpan.FromMinutes(10);

    public override string Name => ToolName;

    protected override string Description =>
        "Searches the catalog of the movie store. Use it for every recommendation: only movies returned by this tool may be recommended. "
        + "Put the topic, mood or plot into query and explicit constraints (genres, years, rating, price, original language) into the other arguments.";

    /// <summary>Genre arrays get an <c>enum</c> of the catalog's slugs: models with constrained decoding then cannot invent a genre (I05 finding).</summary>
    protected override async Task CustomizeSchemaAsync(JsonObject schema, CancellationToken cancellationToken)
    {
        var slugs = await GetGenreSlugsAsync(cancellationToken);
        var properties = schema["properties"]!.AsObject();
        foreach (var name in new[] { "genresInclude", "genresExclude" })
        {
            properties[name]!["items"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray([.. slugs.Order(StringComparer.Ordinal).Select(s => JsonValue.Create(s))]),
            };
        }
    }

    protected override async Task<ToolOutcome> ExecuteAsync(SearchMoviesArgs arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var query = arguments.Query?.Trim() ?? "";
        if (query.Length == 0)
        {
            return ToolOutcome.Error("`query` is required: describe in a few words what the movie should be about.");
        }

        if (query.Length > MaxQueryLength)
        {
            query = query[..MaxQueryLength];
        }

        var notes = new List<string>();
        var slugs = await GetGenreSlugsAsync(cancellationToken);
        var (yearFrom, yearTo) = (arguments.YearFrom, arguments.YearTo);
        if (yearFrom > yearTo)
        {
            (yearFrom, yearTo) = (yearTo, yearFrom);
        }

        var filter = new MovieFilter
        {
            GenreSlugs = KnownGenres(arguments.GenresInclude, slugs, notes),
            ExcludedGenreSlugs = KnownGenres(arguments.GenresExclude, slugs, notes),
            YearFrom = yearFrom,
            YearTo = yearTo,
            MinRating = arguments.MinRating is { } rating ? Math.Clamp(rating, 0, 10) : null,
            PriceMax = PriceMax(arguments.MaxPrice, notes),
            Language = Language(arguments.Language, notes),
            ExcludedMovieIds = arguments.ExcludeMovieIds ?? [],
        };
        var limit = Math.Clamp(arguments.Limit ?? options.Value.SearchResultLimit, 1, MaxLimit);

        var result = await search.SearchAsync(new SearchQuery(query, SearchMode.Hybrid, filter, context.Locale, Page: 1, PageSize: limit), cancellationToken);
        var movies = result.Items.Select(i => i.Movie).ToList();

        if (movies.Count == 0)
        {
            notes.Add("No movies match. Tell the user and suggest relaxing the request (fewer filters, another genre or period).");
        }

        var payload = new
        {
            movies = movies.Select(Compact),
            notes = notes.Count > 0 ? notes : null,
        };

        return new ToolOutcome(JsonSerializer.Serialize(payload, ToolJson.Options), true, [.. movies.Select(MovieCard.From)]);
    }

    /// <summary>Only what the model needs to choose and explain; the overview is cut to keep the context small.</summary>
    private static object Compact(MovieSummary movie) => new
    {
        id = movie.Id,
        title = movie.Title,
        year = movie.Year,
        genres = movie.Genres.Select(g => g.Name),
        rating = Math.Round(movie.VoteAverage, 1),
        overview = Shorten(movie.Overview),
        offers = movie.Offers.Select(FormatOffer),
    };

    internal static string FormatOffer(OfferItem offer)
    {
        var price = (offer.PriceMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        return offer.Type == OfferType.Rent && offer.RentalHours is { } hours
            ? $"rent for {hours.ToString(CultureInfo.InvariantCulture)} h: {price} {offer.Currency}"
            : $"{offer.Type.ToString().ToLowerInvariant()}: {price} {offer.Currency}";
    }

    internal static string? Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= MaxOverviewLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', MaxOverviewLength - 1);
        return string.Concat(text.AsSpan(0, cut > MaxOverviewLength / 2 ? cut : MaxOverviewLength - 1), "…");
    }

    private static List<string> KnownGenres(IReadOnlyList<string>? requested, IReadOnlySet<string> known, List<string> notes)
    {
        if (requested is null || requested.Count == 0)
        {
            return [];
        }

        var normalized = requested.Select(g => g.Trim().ToLowerInvariant()).Distinct().ToList();
        var unknown = normalized.Where(g => !known.Contains(g)).ToList();
        if (unknown.Count > 0)
        {
            notes.Add($"Unknown genres ignored: {string.Join(", ", unknown)}. Use the slugs of the schema.");
        }

        return [.. normalized.Where(known.Contains)];
    }

    private static long? PriceMax(decimal? maxPrice, List<string> notes)
    {
        if (maxPrice is null)
        {
            return null;
        }

        if (maxPrice <= 0)
        {
            notes.Add("maxPrice must be positive; ignored.");
            return null;
        }

        return (long)Math.Round(maxPrice.Value * 100m, MidpointRounding.AwayFromZero);
    }

    private static string? Language(string? language, List<string> notes)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var code = language.Trim().ToLowerInvariant();
        if (code.Length == 2 && code.All(char.IsAsciiLetterLower))
        {
            return code;
        }

        notes.Add($"language '{language}' is not an ISO 639-1 code; ignored.");
        return null;
    }

    private async Task<IReadOnlySet<string>> GetGenreSlugsAsync(CancellationToken cancellationToken)
    {
        var slugs = await cache.GetOrCreateAsync("assistant:genre-slugs", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = GenreCacheTime;
            var genres = await catalog.ListGenresAsync(Locales.Default, cancellationToken);
            return (IReadOnlySet<string>)genres.Select(g => g.Slug).ToHashSet(StringComparer.Ordinal);
        });

        return slugs!;
    }
}
