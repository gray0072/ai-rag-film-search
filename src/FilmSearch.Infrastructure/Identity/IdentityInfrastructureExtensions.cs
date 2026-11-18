using FilmSearch.Core.Owners;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FilmSearch.Infrastructure.Identity;

public static class IdentityInfrastructureExtensions
{
    /// <summary>
    /// ASP.NET Core Identity core services with the EF Core stores in <see cref="AppDbContext"/>, the sign-in manager,
    /// roles, the login merge (<see cref="IOwnerMerger"/>) and the admin account options (I17). The authentication
    /// cookie is configured by the API host.
    /// </summary>
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddIdentityCore<AppUser>(ConfigureIdentity)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddScoped<IOwnerMerger, OwnerMerger>();

        services.AddOptions<AdminAccountOptions>().Bind(configuration.GetSection(AdminAccountOptions.SectionName));
        services.AddScoped<AdminAccountSeeder>();

        return services;
    }

    /// <summary>
    /// Password rules follow NIST SP 800-63B: a minimum length and no composition rules (they do not make passwords
    /// stronger, only harder to remember). Lockout slows down password guessing. Emails are not confirmed yet (no mail
    /// sender), so an email is only a login name.
    /// </summary>
    internal static void ConfigureIdentity(IdentityOptions options)
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = AuthLimits.PasswordMinLength;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.SignIn.RequireConfirmedEmail = false;
    }
}

/// <summary>Input limits of the auth endpoints.</summary>
public static class AuthLimits
{
    public const int PasswordMinLength = 8;

    /// <summary>Hashing very long passwords costs CPU for nothing; 128 is far above any password manager default.</summary>
    public const int PasswordMaxLength = 128;

    /// <summary>Length of the Identity user name and email columns.</summary>
    public const int EmailMaxLength = 256;
}
