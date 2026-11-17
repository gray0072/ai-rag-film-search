using FilmSearch.Api.Localization;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Search;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FilmSearch.Api.Modules.Search;

/// <summary>Search endpoint (spec 04 "Search", spec 05): keyword mode from I04.</summary>
public static class SearchModule
{
    public static IServiceCollection AddSearchModule(this IServiceCollection services) =>
        services.AddSearchInfrastructure();

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/search", Search)
            .WithTags("Search")
            .WithName("SearchMovies")
            .WithSummary("Searches movies by free text with catalog filters. Movie texts in the Accept-Language locale.");

        return app;
    }

    private static async Task<Results<Ok<SearchResponse>, ValidationProblem>> Search(
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
                ["mode"] = [$"Search mode '{query.Mode.ToString().ToLowerInvariant()}' is not available yet. Use 'keyword'."],
            });
        }

        if (request.Understand)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["understand"] = ["Query understanding is not available yet."],
            });
        }

        var result = await search.SearchAsync(query, cancellationToken);
        return TypedResults.Ok(new SearchResponse(
            result.Items,
            SearchFilters.From(query.Filter),
            result.Page,
            result.PageSize,
            result.Total));
    }
}
