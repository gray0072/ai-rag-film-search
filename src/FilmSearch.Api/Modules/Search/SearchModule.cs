using System.ComponentModel.DataAnnotations;
using FilmSearch.Api.Localization;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Search;
using FilmSearch.Infrastructure.Search.Semantic;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace FilmSearch.Api.Modules.Search;

/// <summary>Search endpoints (spec 04 "Search", spec 05): keyword mode from I04, semantic mode and similar movies from I06, hybrid mode and query understanding from I07.</summary>
public static class SearchModule
{
    public const int DefaultSimilarLimit = 10;
    public const int MaxSimilarLimit = 50;

    public static IServiceCollection AddSearchModule(this IServiceCollection services, IConfiguration configuration) =>
        services.AddSearchInfrastructure(configuration);

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/search", Search)
            .WithTags("Search")
            .WithName("SearchMovies")
            .WithSummary("Searches movies by free text with catalog filters. Movie texts in the Accept-Language locale.");

        app.MapGet("/api/movies/{id:guid}/similar", Similar)
            .WithTags("Search")
            .WithName("GetSimilarMovies")
            .WithSummary("Movies nearest to this one by embedding (cosine similarity), best first. Empty while the movie has no vector.");

        return app;
    }

    /// <summary>Queues a reindex of stale vectors when <c>Search:Embeddings:IndexOnStartup</c> is on (Development), after the seed.</summary>
    public static void IndexEmbeddingsOnStartup(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<EmbeddingIndexOptions>>().Value.IndexOnStartup)
        {
            app.Services.GetRequiredService<IEmbeddingJobs>().TryStart(new ReindexRequest(), out _);
        }
    }

    private static async Task<Results<Ok<SearchResponse>, ValidationProblem, ProblemHttpResult>> Search(
        SearchRequest request,
        IMovieSearch search,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var query = request.ToQuery(httpContext.GetLocale());

        if (!search.Supports(query.Mode))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mode"] = [$"Search mode '{query.Mode.ToString().ToLowerInvariant()}' is not available."],
            });
        }

        try
        {
            var result = await search.SearchAsync(query, cancellationToken);
            return TypedResults.Ok(new SearchResponse(
                result.Items,
                SearchFilters.From(result.AppliedFilter),
                result.Page,
                result.PageSize,
                result.Total,
                result.Understood,
                result.Degraded));
        }
        catch (AiProviderUnavailableException ex)
        {
            return EmbeddingsUnavailable(ex);
        }
    }

    private static async Task<Results<Ok<IReadOnlyList<SearchItem>>, NotFound, ValidationProblem>> Similar(
        Guid id,
        IMovieSearch search,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        [Range(1, MaxSimilarLimit)] int? limit = null)
    {
        var items = await search.FindSimilarAsync(id, MovieFilter.None, httpContext.GetLocale(), limit ?? DefaultSimilarLimit, cancellationToken);
        return items is null ? TypedResults.NotFound() : TypedResults.Ok(items);
    }

    /// <summary>503: semantic mode could not embed the query (LM Studio not running, provider down). Keyword search still works; hybrid search degrades to keyword results.</summary>
    private static ProblemHttpResult EmbeddingsUnavailable(AiProviderUnavailableException ex) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Semantic search is not available: the embedding provider failed.",
            detail: ex.Message);
}
