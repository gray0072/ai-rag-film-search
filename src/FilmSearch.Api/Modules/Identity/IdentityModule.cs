using FilmSearch.Api.OpenApi;
using FilmSearch.Api.Owners;
using FilmSearch.Core.Owners;
using FilmSearch.Infrastructure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace FilmSearch.Api.Modules.Identity;

/// <summary>
/// Accounts (FR-USR-2, FR-USR-3, ADR-0010): register, log in and out with an HttpOnly cookie for the SPA, the current
/// user, the admin policy. Signing in merges the visitor's anonymous cart, orders, library and conversations into the
/// account (<see cref="IOwnerMerger"/>).
/// </summary>
public static class IdentityModule
{
    /// <summary>Authorization policy of the admin endpoints.</summary>
    public const string AdminPolicy = "admin";

    public const string AuthCookieName = "fs_auth";

    public static readonly TimeSpan AuthCookieLifetime = TimeSpan.FromDays(14);

    public static WebApplicationBuilder AddIdentityModule(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentityInfrastructure(builder.Configuration);

        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = AuthCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            // Lax: not sent with cross-site POSTs, which is the CSRF protection of the cookie-authenticated API.
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = AuthCookieLifetime;
            options.SlidingExpiration = true;
            // An API: answer 401 / 403 instead of redirecting to a login page.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(Roles.Admin));

        return builder;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", Register)
            .WithName("Register")
            .WithSummary("Creates an account and signs it in; the visitor's anonymous cart, orders and conversations move to it.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/login", Login)
            .WithName("Login")
            .WithSummary("Signs in with email and password (cookie); the visitor's anonymous cart, orders and conversations move to the account.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapPost("/logout", Logout)
            .WithName("Logout")
            .WithSummary("Signs out. The next request starts a new anonymous session with an empty cart.");
        group.MapGet("/me", Me)
            .WithName("GetCurrentUser")
            .WithSummary("The signed-in user with roles, or authenticated false.");

        return app;
    }

    /// <summary>Creates the admin role and the configured admin account (after migrations).</summary>
    public static async Task SeedIdentityAsync(this WebApplication app)
    {
        if (BuildTimeOpenApi.IsGeneratingDocument)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AdminAccountSeeder>().SeedAsync(app.Lifetime.ApplicationStopping);
    }

    private static async Task<Results<Ok<CurrentUser>, ValidationProblem, ProblemHttpResult>> Register(
        RegisterRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        TimeProvider time,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var email = request.Email!.Trim();
        var now = time.GetUtcNow();
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(now),
            UserName = email,
            Email = email,
            CreatedAt = now,
        };

        var result = await users.CreateAsync(user, request.Password!);
        if (!result.Succeeded)
        {
            return RegisterProblem(result);
        }

        await SignInAsync(user, signIn, httpContext, cancellationToken);
        return TypedResults.Ok(await CurrentUserAsync(user, users));
    }

    private static async Task<Results<Ok<CurrentUser>, ProblemHttpResult>> Login(
        LoginRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email!.Trim());
        if (user is null)
        {
            return LoginProblem(AuthErrorCodes.InvalidCredentials);
        }

        // Checks the password and the lockout without signing in yet: the merge runs first.
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return LoginProblem(AuthErrorCodes.LockedOut);
        }

        if (!result.Succeeded)
        {
            return LoginProblem(AuthErrorCodes.InvalidCredentials);
        }

        await SignInAsync(user, signIn, httpContext, cancellationToken);
        return TypedResults.Ok(await CurrentUserAsync(user, users));
    }

    private static async Task<NoContent> Logout(SignInManager<AppUser> signIn, HttpContext httpContext)
    {
        await signIn.SignOutAsync();
        OwnerCookie.Delete(httpContext.Response);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<CurrentUser>> Me(HttpContext httpContext, UserManager<AppUser> users)
    {
        if (!OwnerCookieMiddleware.TryGetUserId(httpContext.User, out _) || await users.GetUserAsync(httpContext.User) is not { } user)
        {
            return TypedResults.Ok(CurrentUser.Anonymous);
        }

        return TypedResults.Ok(await CurrentUserAsync(user, users));
    }

    /// <summary>
    /// FR-USR-2: the anonymous visitor's data becomes the user's, then the cookie is issued (a failed merge signs nobody
    /// in). Only an anonymous request merges: signing in as another user while signed in must not move one account's
    /// data to the other. The anonymous cookie is removed; after a logout the visitor starts with a new, empty owner.
    /// </summary>
    private static async Task SignInAsync(AppUser user, SignInManager<AppUser> signIn, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var owner = httpContext.RequestServices.GetRequiredService<OwnerContext>();
        if (owner.IsAnonymous)
        {
            await httpContext.RequestServices.GetRequiredService<IOwnerMerger>().MergeAsync(owner.OwnerId, user.Id, cancellationToken);
        }

        await signIn.SignInAsync(user, isPersistent: true);
        OwnerCookie.Delete(httpContext.Response);
    }

    private static async Task<CurrentUser> CurrentUserAsync(AppUser user, UserManager<AppUser> users) =>
        new(true, user.Email, [.. (await users.GetRolesAsync(user)).Order(StringComparer.Ordinal)]);

    private static Results<Ok<CurrentUser>, ValidationProblem, ProblemHttpResult> RegisterProblem(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "An account with this email already exists.",
                extensions: new Dictionary<string, object?> { ["code"] = AuthErrorCodes.EmailTaken });
        }

        // Password rules and email format: field errors like the request validation.
        return TypedResults.ValidationProblem(result.Errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "Password" : "Email")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
    }

    private static ProblemHttpResult LoginProblem(string code) => TypedResults.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: code == AuthErrorCodes.LockedOut
            ? "Too many failed attempts. Try again in a few minutes."
            : "The email or password is incorrect.",
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
