using System.ComponentModel.DataAnnotations;
using FilmSearch.Infrastructure.Identity;

namespace FilmSearch.Api.Modules.Identity;

/// <summary>Body of <c>POST /api/auth/register</c>.</summary>
public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(AuthLimits.EmailMaxLength)]
    public string? Email { get; init; }

    /// <summary>At least 8 characters, no composition rules.</summary>
    [Required]
    [StringLength(AuthLimits.PasswordMaxLength, MinimumLength = AuthLimits.PasswordMinLength)]
    public string? Password { get; init; }
}

/// <summary>Body of <c>POST /api/auth/login</c>.</summary>
public sealed class LoginRequest
{
    [Required]
    [MaxLength(AuthLimits.EmailMaxLength)]
    public string? Email { get; init; }

    [Required]
    [MaxLength(AuthLimits.PasswordMaxLength)]
    public string? Password { get; init; }
}

/// <summary>The visitor: a signed-in user (email, roles) or anonymous (<c>authenticated: false</c>).</summary>
/// <param name="Authenticated">Whether a user is signed in.</param>
/// <param name="Email">Email of the user; null when anonymous.</param>
/// <param name="Roles">Roles of the user, e.g. <c>admin</c>; empty when anonymous.</param>
public sealed record CurrentUser(bool Authenticated, string? Email, IReadOnlyList<string> Roles)
{
    public static readonly CurrentUser Anonymous = new(false, null, []);
}

/// <summary>Machine-readable <c>code</c> of auth Problem Details.</summary>
public static class AuthErrorCodes
{
    /// <summary>401 on login: unknown email or wrong password (the two are not told apart).</summary>
    public const string InvalidCredentials = "invalidCredentials";

    /// <summary>401 on login: too many failed attempts, try again later.</summary>
    public const string LockedOut = "lockedOut";

    /// <summary>409 on register: an account with this email exists.</summary>
    public const string EmailTaken = "emailTaken";
}
