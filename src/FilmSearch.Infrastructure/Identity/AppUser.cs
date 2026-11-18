using Microsoft.AspNetCore.Identity;

namespace FilmSearch.Infrastructure.Identity;

/// <summary>
/// A registered user (ASP.NET Core Identity, schema <c>identity</c>). The id is the owner id of the user's cart, orders,
/// library and conversations (<see cref="Core.Owners.IOwnerContext"/>). The user name is the email.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Role names.</summary>
public static class Roles
{
    /// <summary>Admin endpoints: ingestion, embeddings, AI diagnostics (FR-USR-3).</summary>
    public const string Admin = "admin";
}
