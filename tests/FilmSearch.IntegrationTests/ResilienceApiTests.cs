using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FilmSearch.Api.Modules.Search;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Search;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.IntegrationTests;

/// <summary>
/// Chaos tests over HTTP (I19, NFR-6): the chat profiles fail the way providers fail (refused connection, timeout, open
/// circuit) and the user-facing behavior is checked: the fallback profile answers and the answer is flagged, or the turn
/// ends with a clear error; query understanding falls back; repeated queries are served from the cache.
/// The search side (embeddings down → keyword results, degraded) is in <see cref="EmbeddingIndexTests"/>.
/// </summary>
public sealed class ResilienceApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonSerializerOptions Json => CatalogApiFixture.Json;

    private FilmSearchApiFactory _factory = null!;
    private SwitchableChatClient _local = null!;
    private SwitchableChatClient _openRouter = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new FilmSearchApiFactory(
            postgres,
            settings: new Dictionary<string, string?>
            {
                ["AI:FallbackChatProfile"] = "openrouter",
                ["AI:ChatProfiles:openrouter:ApiKey"] = "test-key",
            },
            configureServices: services =>
            {
                // Both profiles answer with the scripted model while they are up.
                services.AddKeyedSingleton<IChatClient>("local", (_, _) => _local = new SwitchableChatClient(_factory.Chat));
                services.AddKeyedSingleton<IChatClient>("openrouter", (_, _) => _openRouter = new SwitchableChatClient(_factory.Chat));
            });
        _ = _factory.Services.GetRequiredService<IChatClient>(); // creates both profile clients
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Theory]
    [InlineData(ChatProviderState.Refused)]
    [InlineData(ChatProviderState.TimedOut)]
    [InlineData(ChatProviderState.CircuitOpen)]
    public async Task When_the_active_profile_fails_the_fallback_answers_and_the_answer_is_flagged(ChatProviderState failure)
    {
        var question = $"Something to watch tonight (fallback test, {failure})";
        _factory.Chat.ScriptStream(question, [new TextContent("Try a comedy tonight.")]);
        _local.State = failure;
        _openRouter.State = ChatProviderState.Up;
        using var recorder = new MeterRecorder(_factory.Services, "ai.fallbacks");
        using var client = CreateClient();
        var conversation = await CreateConversationAsync(client);

        var events = await SendAsync(client, conversation.Id, question);

        events.Select(e => e.Name).ShouldBe(["message.started", "message.delta", "message.completed"]);
        events[^1].Data.GetProperty("fallback").GetBoolean().ShouldBeTrue();
        var view = await client.GetFromJsonAsync<ConversationView>($"/api/chat/conversations/{conversation.Id}", Json, Ct);
        var answer = view!.Messages.Single(m => m.Role == MessageRole.Assistant);
        answer.Text.ShouldBe("Try a comedy tonight.");
        answer.Fallback.ShouldBeTrue();
        var fallback = recorder.Of("ai.fallbacks").ShouldHaveSingleItem();
        fallback.Tag("from").ShouldBe("local");
        fallback.Tag("to").ShouldBe("openrouter");
    }

    [Fact]
    public async Task When_every_chat_profile_is_down_the_turn_ends_with_a_clear_error_and_the_question_is_kept()
    {
        const string question = "Recommend a thriller (all providers down)";
        _local.State = ChatProviderState.TimedOut;
        _openRouter.State = ChatProviderState.Refused;
        using var client = CreateClient();
        var conversation = await CreateConversationAsync(client);

        var events = await SendAsync(client, conversation.Id, question);

        events.Select(e => e.Name).ShouldBe(["message.started", "error"]);
        events[^1].Data.GetProperty("code").GetString().ShouldBe(AssistantErrorCodes.ProviderUnavailable);
        var view = await client.GetFromJsonAsync<ConversationView>($"/api/chat/conversations/{conversation.Id}", Json, Ct);
        view!.Messages.ShouldHaveSingleItem().Text.ShouldBe(question);
    }

    [Fact]
    public async Task Query_understanding_falls_back_when_every_chat_profile_is_down()
    {
        _local.State = ChatProviderState.Refused;
        _openRouter.State = ChatProviderState.CircuitOpen;
        using var client = CreateClient();

        using var response = await client.PostAsJsonAsync("/api/search", new { query = "funny movies from the nineties (qu down)", mode = "keyword", understand = true }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<SearchResponse>(Json, Ct))!;
        result.UnderstoodQuery!.Source.ShouldBe(QueryUnderstandingSource.Fallback);
        result.UnderstoodQuery.Error!.ShouldContain("provider failed");
    }

    [Fact]
    public async Task A_repeated_query_is_embedded_once_and_the_cache_hit_is_counted()
    {
        using var recorder = new MeterRecorder(_factory.Services, "cache.lookups");
        using var client = CreateClient();

        foreach (var query in new[] { "Whales in a stormy OCEAN (cache test)", "whales in a stormy ocean  (cache test)" })
        {
            using var response = await client.PostAsJsonAsync("/api/search", new { query, mode = "semantic" }, Json, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadFromJsonAsync<SearchResponse>(Json, Ct))!.Degraded.ShouldBeFalse();
        }

        recorder.Of("cache.lookups")
            .Where(m => (string?)m.Tag("cache") == "query_embedding")
            .Select(m => m.Tag("result"))
            .ShouldBe(["miss", "hit"]);
    }

    private HttpClient CreateClient() =>
        // https: the owner cookie is Secure. Each client is a new owner.
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<ConversationListItem> CreateConversationAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/chat/conversations", content: null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ConversationListItem>(Json, Ct))!;
    }

    private static async Task<List<(string Name, JsonElement Data)>> SendAsync(HttpClient client, Guid conversationId, string text)
    {
        using var response = await client.PostAsJsonAsync($"/api/chat/conversations/{conversationId}/messages", new { text }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return AssistantApiTests.ParseEvents(await response.Content.ReadAsStringAsync(Ct));
    }
}
