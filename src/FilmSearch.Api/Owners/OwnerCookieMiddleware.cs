namespace FilmSearch.Api.Owners;

/// <summary>
/// Resolves the owner of an <c>/api</c> request from the <c>fs_sid</c> cookie.
/// A missing or malformed cookie gets a new random id, which is sent back as a cookie.
/// I17 (authentication) will prefer the signed-in user id here.
/// </summary>
public sealed class OwnerCookieMiddleware(RequestDelegate next)
{
    private static readonly PathString ApiPath = "/api";

    public Task InvokeAsync(HttpContext context, OwnerContext owner)
    {
        if (context.Request.Path.StartsWithSegments(ApiPath))
        {
            if (!OwnerCookie.TryParse(context.Request.Cookies[OwnerCookie.Name], out var ownerId))
            {
                ownerId = OwnerCookie.NewId();
                context.Response.Cookies.Append(OwnerCookie.Name, OwnerCookie.Format(ownerId), OwnerCookie.CreateOptions());
            }

            owner.Set(ownerId);
        }

        return next(context);
    }
}
