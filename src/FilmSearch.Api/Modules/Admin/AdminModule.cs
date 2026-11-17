using FilmSearch.Api.OpenApi;
using FilmSearch.Core.Ingestion;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FilmSearch.Api.Modules.Admin;

/// <summary>
/// Admin endpoints (spec 04 "Admin"). There is no authentication before I17, so they are mapped only when
/// <c>Admin:Enabled</c> is true (Development and integration tests). They are always in the OpenAPI document,
/// so the generated contract does not depend on the environment of the build.
/// </summary>
public static class AdminModule
{
    public const string EnabledKey = "Admin:Enabled";

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>(EnabledKey) && !BuildTimeOpenApi.IsGeneratingDocument)
        {
            return app;
        }

        var group = app.MapGroup("/api/admin").WithTags("Admin");

        group.MapPost("/ingestion/seed", LoadSeed)
            .WithName("LoadSeed")
            .WithSummary("Loads the seed file into the catalog. Idempotent: upsert by TMDB id.");

        group.MapAdminAiEndpoints();

        return app;
    }

    private static async Task<Ok<SeedResult>> LoadSeed(ICatalogSeeder seeder, CancellationToken cancellationToken) =>
        TypedResults.Ok(await seeder.LoadAsync(cancellationToken));
}
