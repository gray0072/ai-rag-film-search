using System.Globalization;
using FilmSearch.Core.Localization;
using Microsoft.AspNetCore.Localization;

namespace FilmSearch.Api.Localization;

/// <summary>
/// The request locale comes from <c>Accept-Language</c> (the web client sends the UI language, ADR-0007).
/// Supported: <c>en</c>, <c>ru</c>, <c>sv</c>; regional tags fall back to the language (<c>sv-SE</c> → <c>sv</c>);
/// anything else → <c>en</c>. The response carries <c>Content-Language</c>.
/// </summary>
public static class RequestLocaleExtensions
{
    public static IServiceCollection AddRequestLocales(this IServiceCollection services) =>
        services.Configure<RequestLocalizationOptions>(options =>
        {
            var cultures = Locales.All.Select(locale => new CultureInfo(locale)).ToList();
            options.DefaultRequestCulture = new RequestCulture(Locales.Default);
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
            options.FallBackToParentCultures = true;
            options.FallBackToParentUICultures = true;
            options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
            options.ApplyCurrentCultureToResponseHeaders = true;
        });

    /// <summary>The supported locale of the current request (<c>en</c>, <c>ru</c> or <c>sv</c>).</summary>
    public static string GetLocale(this HttpContext httpContext) =>
        Locales.Normalize(httpContext.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture.Name);
}
