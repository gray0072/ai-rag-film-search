using System.Security.Claims;

namespace FilmSearch.Api.Owners;

/// <summary>
/// Resolves the owner of an <c>/api</c> request: the user id of a signed-in user (I17), otherwise the anonymous session
/// id from the <c>fs_sid</c> cookie. A missing or malformed cookie gets a new random id, which is sent back as a cookie.
/// Runs after the authentication middleware.
/// </summary>
public sealed class OwnerCookieMiddleware(RequestDelegate next)
{
    private static readonly PathString ApiPath = "/api";

    public Task InvokeAsync(HttpContext context, OwnerContext owner)
    {
        if (context.Request.Path.StartsWithSegments(ApiPath))
        {
            if (TryGetUserId(context.User, out var userId))
            {
                owner.Set(userId, isAnonymous: false);
            }
            else
            {
                if (!OwnerCookie.TryParse(context.Request.Cookies[OwnerCookie.Name], out var ownerId))
                {
                    ownerId = OwnerCookie.NewId();
                    context.Response.Cookies.Append(OwnerCookie.Name, OwnerCookie.Format(ownerId), OwnerCookie.CreateOptions());
                }

                owner.Set(ownerId, isAnonymous: true);
            }
        }

        return next(context);
    }

    /// <summary>The Identity user id (<see cref="ClaimTypes.NameIdentifier"/>) of an authenticated request.</summary>
    internal static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        userId = Guid.Empty;
        return user.Identity?.IsAuthenticated == true
            && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out userId)
            && userId != Guid.Empty;
    }
}
