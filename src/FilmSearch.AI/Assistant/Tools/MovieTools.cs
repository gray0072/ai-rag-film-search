using System.ComponentModel;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using Microsoft.Extensions.Options;

namespace FilmSearch.AI.Assistant.Tools;

/// <summary>Arguments of <c>get_movie_details</c>.</summary>
internal sealed record MovieIdArgs(
    [property: Description("Id of the movie, exactly as a tool returned it (search_movies, find_similar_movies, view_cart).")]
    Guid MovieId);

/// <summary>Arguments of <c>find_similar_movies</c>.</summary>
internal sealed record FindSimilarMoviesArgs(
    [property: Description("Id of the movie to find similar ones for, exactly as a tool returned it.")]
    Guid MovieId,
    [property: Description("How many movies to return, 1-10. Default 6.")]
    int? Limit = null);

/// <summary>
/// <c>get_movie_details</c> (spec 06 §3): the full card of one movie (director, cast, runtime, tagline, longer overview)
/// for questions like "who plays in it?" or "how long is it?". No card event: the movie is already on screen.
/// </summary>
internal sealed class GetMovieDetailsTool(ICatalogReader catalog) : AssistantTool<MovieIdArgs>
{
    public const string ToolName = "get_movie_details";

    public const int MaxOverviewLength = 1000;
    public const int MaxCast = 6;
    public const int MaxKeywords = 10;

    public override string Name => ToolName;

    protected override string Description =>
        "Gets details of one movie of the catalog: director, writers, main cast, runtime, tagline, full overview, keywords and prices. "
        + "Use it when the user asks about a movie that a tool already returned.";

    protected override async Task<ToolOutcome> ExecuteAsync(MovieIdArgs arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.MovieId == Guid.Empty)
        {
            return ToolOutcome.Error($"`movieId` is required: {MovieIdHint}.");
        }

        var movie = await catalog.GetMovieAsync(arguments.MovieId, context.Locale, cancellationToken);
        if (movie is null)
        {
            return ToolOutcome.Error($"Movie {arguments.MovieId} was not found. Use a movie id from a tool result.");
        }

        var payload = new
        {
            id = movie.Id,
            title = movie.Title,
            originalTitle = movie.OriginalTitle != movie.Title ? movie.OriginalTitle : null,
            year = movie.Year,
            originalLanguage = movie.OriginalLanguage,
            runtimeMinutes = movie.RuntimeMinutes,
            genres = movie.Genres.Select(g => g.Name),
            rating = Math.Round(movie.VoteAverage, 1),
            votes = movie.VoteCount,
            tagline = movie.Tagline,
            overview = ToolResults.Shorten(movie.Overview, MaxOverviewLength),
            directors = movie.Directors.Select(p => p.Name),
            writers = movie.Writers.Take(3).Select(p => p.Name),
            cast = movie.Cast.Take(MaxCast).Select(c => string.IsNullOrWhiteSpace(c.Character) ? c.Name : $"{c.Name} as {c.Character}"),
            keywords = movie.Keywords.Take(MaxKeywords),
            offers = movie.Offers.Select(ToolResults.FormatOffer),
        };

        return ToolOutcome.Success(payload);
    }
}

/// <summary>
/// <c>find_similar_movies</c> (spec 06 §3): nearest neighbours of a movie in the embedding space (I06), for "more like
/// this one". Same result shape and cards as <c>search_movies</c>.
/// </summary>
internal sealed class FindSimilarMoviesTool(IMovieSearch search, IOptions<AssistantOptions> options) : AssistantTool<FindSimilarMoviesArgs>
{
    public const string ToolName = "find_similar_movies";

    public override string Name => ToolName;

    protected override string Description =>
        "Finds movies of the catalog that are similar to a given movie (plot, mood, themes). "
        + "Use it when the user asks for movies like one that a tool already returned. Only returned movies may be recommended.";

    protected override async Task<ToolOutcome> ExecuteAsync(FindSimilarMoviesArgs arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.MovieId == Guid.Empty)
        {
            return ToolOutcome.Error($"`movieId` is required: {MovieIdHint}.");
        }

        var limit = Math.Clamp(arguments.Limit ?? options.Value.SearchResultLimit, 1, SearchMoviesTool.MaxLimit);
        var items = await search.FindSimilarAsync(arguments.MovieId, new MovieFilter(), context.Locale, limit, cancellationToken);
        if (items is null)
        {
            return ToolOutcome.Error($"Movie {arguments.MovieId} was not found. Use a movie id from a tool result.");
        }

        var movies = items.Select(i => i.Movie).ToList();
        var payload = new
        {
            movies = movies.Select(ToolResults.Movie),
            notes = movies.Count == 0
                ? "No similar movies are known for this movie yet. Use search_movies with its topic or genre instead."
                : null,
        };

        return ToolOutcome.Success(payload, [.. movies.Select(MovieCard.From)]);
    }
}
