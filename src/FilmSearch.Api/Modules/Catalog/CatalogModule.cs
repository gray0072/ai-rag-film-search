using FilmSearch.Api.Localization;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Ingestion;
using FilmSearch.Infrastructure.Catalog;
using FilmSearch.Infrastructure.Ingestion;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace FilmSearch.Api.Modules.Catalog;

/// <summary>Catalog endpoints (spec 04 "Catalog"): movie list with filters and sorting, movie details, genres.</summary>
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration) =>
        services.AddCatalogInfrastructure(configuration);

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Catalog");

        group.MapGet("/movies", ListMovies)
            .WithName("ListMovies")
            .WithSummary("Paged movie list with filters and sorting. Texts in the Accept-Language locale.");

        group.MapGet("/movies/{id:guid}", GetMovie)
            .WithName("GetMovie")
            .WithSummary("Movie details with genres, credits and offers. Texts in the Accept-Language locale.");

        group.MapGet("/genres", ListGenres)
            .WithName("ListGenres")
            .WithSummary("Genres with names in the Accept-Language locale.");

        return app;
    }

    /// <summary>Loads the seed file when <c>Catalog:Seed:LoadOnStartup</c> is on and the catalog is empty (FR-DATA-1).</summary>
    public static async Task SeedCatalogOnStartupAsync(this WebApplication app)
    {
        if (!app.Services.GetRequiredService<IOptions<CatalogSeedOptions>>().Value.LoadOnStartup)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICatalogSeeder>().LoadIfEmptyAsync(app.Lifetime.ApplicationStopping);
    }

    private static async Task<Ok<PagedResult<MovieSummary>>> ListMovies(
        [AsParameters] MovieListRequest request,
        ICatalogReader catalog,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await catalog.ListMoviesAsync(request.ToQuery(), httpContext.GetLocale(), cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<MovieDetails>, NotFound>> GetMovie(
        Guid id,
        ICatalogReader catalog,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var movie = await catalog.GetMovieAsync(id, httpContext.GetLocale(), cancellationToken);
        return movie is null ? TypedResults.NotFound() : TypedResults.Ok(movie);
    }

    private static async Task<Ok<IReadOnlyList<GenreItem>>> ListGenres(
        ICatalogReader catalog,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalog.ListGenresAsync(httpContext.GetLocale(), cancellationToken));
}
