using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FilmSearch.Api.RateLimiting;
using FilmSearch.Core.Assistant;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;

namespace FilmSearch.IntegrationTests;

/// <summary>
/// Rate limiting (spec 10 §4, NFR-7, I19): chat messages per owner, new conversations and searches per client IP, a 429
/// Problem Details with <c>code: rateLimited</c> and <c>Retry-After</c>. Each test has its own app (limiter state) with small limits.
/// </summary>
public sealed class RateLimitingApiTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonSerializerOptions Json => CatalogApiFixture.Json;

    [Fact]
    public async Task Searches_over_the_limit_get_429_with_retry_after()
    {
        await using var factory = CreateFactory(new() { ["RateLimiting:Search:PermitLimit"] = "2" });
        using var client = CreateClient(factory);

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 3; i++)
        {
            last?.Dispose();
            last = await client.PostAsJsonAsync("/api/search", new { query = $"rate limit {i}", mode = "keyword" }, Json, Ct);
            statuses.Add(last.StatusCode);
        }

        using var rejected = last!;
        statuses.ShouldBe([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests]);
        await ShouldBeRateLimitedAsync(rejected);

        // Catalog browsing is not limited.
        (await client.GetAsync("/api/movies?pageSize=1", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Chat_messages_are_limited_per_owner()
    {
        await using var factory = CreateFactory(new() { ["RateLimiting:Chat:PermitLimit"] = "1" });
        factory.Chat.ScriptStream("First message (rate limit)", [new TextContent("Hello!")]);
        using var owner = CreateClient(factory);
        using var other = CreateClient(factory);
        var conversation = await CreateConversationAsync(owner);
        var otherConversation = await CreateConversationAsync(other);

        using var first = await SendAsync(owner, conversation.Id, "First message (rate limit)");
        using var second = await SendAsync(owner, conversation.Id, "Second message (rate limit)");
        using var otherFirst = await SendAsync(other, otherConversation.Id, "First message (rate limit)");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        await ShouldBeRateLimitedAsync(second);
        otherFirst.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Chat.StreamRequests.ShouldNotContain(r => r.Messages.Any(m => m.Text == "Second message (rate limit)"));
    }

    [Fact]
    public async Task New_conversations_are_limited_per_client_ip_whatever_the_owner()
    {
        // A script that drops its cookie is a new owner on every request, but not a new IP.
        await using var factory = CreateFactory(new() { ["RateLimiting:NewConversation:PermitLimit"] = "2" });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var client = CreateClient(factory);
            using var response = await client.PostAsync("/api/chat/conversations", content: null, Ct);
            statuses.Add(response.StatusCode);
        }

        statuses.ShouldBe([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests]);
    }

    private FilmSearchApiFactory CreateFactory(Dictionary<string, string?> limits)
    {
        limits["RateLimiting:Enabled"] = "true";
        return new FilmSearchApiFactory(postgres, settings: limits);
    }

    private static HttpClient CreateClient(FilmSearchApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task ShouldBeRateLimitedAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        response.Headers.RetryAfter.ShouldNotBeNull();
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        problem.RootElement.GetProperty("status").GetInt32().ShouldBe(429);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(RateLimitPolicies.RejectedCode);
        problem.RootElement.GetProperty("retryAfterSeconds").GetInt32().ShouldBeInRange(1, 60);
    }

    private static async Task<ConversationListItem> CreateConversationAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/chat/conversations", content: null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ConversationListItem>(Json, Ct))!;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, Guid conversationId, string text) =>
        client.PostAsJsonAsync($"/api/chat/conversations/{conversationId}/messages", new { text }, Ct);
}
