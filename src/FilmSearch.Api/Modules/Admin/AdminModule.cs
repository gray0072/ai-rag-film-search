using FilmSearch.Api.Modules.Identity;
using FilmSearch.Core.Ingestion;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FilmSearch.Api.Modules.Admin;

/// <summary>
/// Admin endpoints (spec 04 "Admin"). They require the <c>admin</c> role (FR-USR-3, I17): 401 without a signed-in
/// user, 403 for other users.
/// </summary>
public static class AdminModule
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization(IdentityModule.AdminPolicy)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/ingestion/seed", LoadSeed)
            .WithName("LoadSeed")
            .WithSummary("Loads the seed file into the catalog. Idempotent: upsert by TMDB id.");

        group.MapAdminAiEndpoints();
        group.MapAdminEmbeddingEndpoints();
        group.MapAdminJobEndpoints();

        return app;
    }

    private static async Task<Ok<SeedResult>> LoadSeed(ICatalogSeeder seeder, CancellationToken cancellationToken) =>
        TypedResults.Ok(await seeder.LoadAsync(cancellationToken));
}
