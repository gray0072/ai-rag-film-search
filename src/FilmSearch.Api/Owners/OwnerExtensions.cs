using FilmSearch.Core.Owners;

namespace FilmSearch.Api.Owners;

public static class OwnerExtensions
{
    public static IServiceCollection AddOwnerContext(this IServiceCollection services)
    {
        services.AddScoped<OwnerContext>();
        services.AddScoped<IOwnerContext>(sp => sp.GetRequiredService<OwnerContext>());
        return services;
    }

    public static IApplicationBuilder UseOwnerContext(this IApplicationBuilder app) =>
        app.UseMiddleware<OwnerCookieMiddleware>();
}
