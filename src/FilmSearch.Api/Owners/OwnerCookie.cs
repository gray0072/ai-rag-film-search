using System.Security.Cryptography;

namespace FilmSearch.Api.Owners;

/// <summary>The anonymous session cookie <c>fs_sid</c> (spec 04, spec 10 §4).</summary>
public static class OwnerCookie
{
    public const string Name = "fs_sid";

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(365);

    /// <summary>A new id with 128 random bits from a cryptographic RNG, so it cannot be guessed.</summary>
    public static Guid NewId() => new(RandomNumberGenerator.GetBytes(16));

    public static string Format(Guid id) => id.ToString("N");

    public static bool TryParse(string? value, out Guid id) =>
        Guid.TryParseExact(value, "N", out id) && id != Guid.Empty;

    public static CookieOptions CreateOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = MaxAge,
        IsEssential = true,
    };
}
