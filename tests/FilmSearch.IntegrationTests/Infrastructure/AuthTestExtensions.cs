using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>Signed-in clients (I17). The auth cookie is <c>Secure</c>, so these clients use <c>https://localhost</c>.</summary>
public static class AuthTestExtensions
{
    /// <summary>The admin account every <see cref="FilmSearchApiFactory"/> seeds (<c>Auth:Admin</c>).</summary>
    public const string AdminEmail = "admin@filmsearch.test";

    public const string AdminPassword = "integration-admin-password";

    /// <summary>A new browser (own cookies) on HTTPS that does not follow redirects.</summary>
    public static HttpClient CreateBrowser(this WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    /// <summary>A browser signed in as the seeded admin.</summary>
    public static async Task<HttpClient> CreateAdminClientAsync(this WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateBrowser();
        await LoginAsync(client, AdminEmail, AdminPassword);
        return client;
    }

    /// <summary>Registers an account with a unique email in this browser (it is signed in afterwards) and returns the email.</summary>
    public static async Task<string> RegisterAsync(HttpClient client, string password = "correct horse battery")
    {
        var email = $"user-{Guid.NewGuid():N}@filmsearch.test";
        using var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return email;
    }

    public static async Task LoginAsync(HttpClient client, string email, string password = "correct horse battery")
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static async Task LogoutAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/auth/logout", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
