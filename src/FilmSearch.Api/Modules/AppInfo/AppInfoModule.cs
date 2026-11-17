using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FilmSearch.Api.Modules.AppInfo;

/// <summary>
/// Example of the module registration pattern (spec 02 §6): each module has one <c>AddXxxModule</c>
/// for services and one <c>MapXxxEndpoints</c> for its endpoint group. Program.cs only calls these two.
/// </summary>
public static class AppInfoModule
{
    public static IServiceCollection AddAppInfoModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapAppInfoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/info").WithTags("AppInfo");

        group.MapGet("/", GetInfo)
            .WithName("GetAppInfo")
            .WithSummary("Application name and version.");

        return app;
    }

    private static Ok<AppInfoResponse> GetInfo()
    {
        var assembly = typeof(AppInfoModule).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        return TypedResults.Ok(new AppInfoResponse("FilmSearch", version));
    }
}

public sealed record AppInfoResponse(string Name, string Version);
