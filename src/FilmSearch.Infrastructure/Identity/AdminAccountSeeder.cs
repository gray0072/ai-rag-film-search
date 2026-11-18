using FilmSearch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FilmSearch.Infrastructure.Identity;

/// <summary>The admin account from configuration (<c>Auth:Admin</c>; the password belongs in user secrets).</summary>
public sealed class AdminAccountOptions
{
    public const string SectionName = "Auth:Admin";

    public string? Email { get; set; }

    public string? Password { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>
/// Creates the <c>admin</c> role and the configured admin account on startup (FR-USR-3). An existing account with the
/// admin email gets the role only when the configured password matches: someone who registered that email first does
/// not become an admin.
/// </summary>
public sealed partial class AdminAccountSeeder(
    AppDbContext db,
    UserManager<AppUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<AdminAccountOptions> options,
    TimeProvider time,
    ILogger<AdminAccountSeeder> logger)
{
    /// <summary>Key of the advisory lock that makes parallel starts (API replicas, test hosts on one database) seed one at a time.</summary>
    private const long SeedLockKey = 0x46_53_41_44_4D; // "FSADM"

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({SeedLockKey})", cancellationToken);
            try
            {
                await SeedLockedAsync();
            }
            finally
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({SeedLockKey})", CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task SeedLockedAsync()
    {
        if (!await roles.RoleExistsAsync(Roles.Admin))
        {
            Check(await roles.CreateAsync(new IdentityRole<Guid>(Roles.Admin) { Id = Guid.CreateVersion7(time.GetUtcNow()) }));
        }

        var admin = options.Value;
        if (!admin.IsConfigured)
        {
            if (!string.IsNullOrWhiteSpace(admin.Email))
            {
                LogNoPassword(logger, admin.Email);
            }

            return;
        }

        var user = await users.FindByEmailAsync(admin.Email!);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.CreateVersion7(time.GetUtcNow()),
                UserName = admin.Email,
                Email = admin.Email,
                EmailConfirmed = true,
                CreatedAt = time.GetUtcNow(),
            };
            Check(await users.CreateAsync(user, admin.Password!));
            Check(await users.AddToRoleAsync(user, Roles.Admin));
            LogCreated(logger, admin.Email!);
            return;
        }

        if (await users.IsInRoleAsync(user, Roles.Admin))
        {
            return;
        }

        if (await users.CheckPasswordAsync(user, admin.Password!))
        {
            Check(await users.AddToRoleAsync(user, Roles.Admin));
            LogPromoted(logger, admin.Email!);
        }
        else
        {
            LogPasswordMismatch(logger, admin.Email!);
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Seeding the admin account failed: {string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))}");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No admin account: Auth:Admin:Password is not set (user secrets) for {Email}")]
    private static partial void LogNoPassword(ILogger logger, string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the admin account {Email}")]
    private static partial void LogCreated(ILogger logger, string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Added the admin role to {Email}")]
    private static partial void LogPromoted(ILogger logger, string email);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The account {Email} exists but its password is not Auth:Admin:Password: it does not get the admin role")]
    private static partial void LogPasswordMismatch(ILogger logger, string email);
}
