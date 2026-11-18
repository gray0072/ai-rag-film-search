using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FilmSearch.Core.Assistant;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;

namespace FilmSearch.IntegrationTests;

/// <summary>
/// The assistant over HTTP (spec 04 "Assistant", FR-AST-1..3, FR-AST-9): conversations, the SSE stream of a turn with a
/// scripted model (tool call → search over the seeded catalog → answer), stored history with cards, owners, feedback.
/// Every test uses its own message texts: the scripted model is keyed by the last user message.
/// </summary>
public sealed class AssistantApiTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonSerializerOptions Json => CatalogApiFixture.Json;

    public async ValueTask InitializeAsync() => await fixture.EnsureEmbeddingsAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_turn_streams_a_grounded_answer_and_the_conversation_can_be_reloaded()
    {
        const string question = "Посоветуй что-нибудь про космос (api test)";
        fixture.Factory.Chat.ScriptStream(
            question,
            [new FunctionCallContent("call-1", "search_movies", new Dictionary<string, object?> { ["query"] = "interstellar", ["limit"] = 3 })],
            [new TextContent("Вот что "), new TextContent("я нашёл.")]);
        var interstellarId = await fixture.FindMovieIdAsync("Interstellar", Ct);
        using var client = CreateClient("ru");

        var conversation = await CreateConversationAsync(client);
        conversation.Locale.ShouldBe("ru");
        conversation.Title.ShouldBeNull();

        using var response = await client.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new { text = question }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        var events = ParseEvents(await response.Content.ReadAsStringAsync(Ct));

        events.Select(e => e.Name).ShouldBe(["message.started", "tool.started", "tool.completed", "movies", "message.delta", "message.delta", "message.completed"]);
        foreach (var (name, data) in events)
        {
            data.GetProperty("type").GetString().ShouldBe(name); // the data names its event (discriminator)
        }

        var messageId = events[0].Data.GetProperty("messageId").GetGuid();
        events[1].Data.GetProperty("name").GetString().ShouldBe("search_movies");
        events[2].Data.GetProperty("ok").GetBoolean().ShouldBeTrue();
        var cards = events[3].Data.GetProperty("items").Deserialize<List<MovieCard>>(Json)!;
        cards.Count.ShouldBeInRange(1, 3);
        cards.ShouldContain(c => c.Id == interstellarId);
        var interstellar = cards.Single(c => c.Id == interstellarId);
        interstellar.Title.ShouldNotBe("Interstellar"); // Russian title from the catalog (Accept-Language: ru)
        interstellar.Offers.ShouldNotBeEmpty();
        interstellar.PosterPath.ShouldNotBeNullOrEmpty();
        var completed = events[^1].Data;
        completed.GetProperty("messageId").GetGuid().ShouldBe(messageId);
        completed.GetProperty("usage").GetProperty("inputTokens").GetInt64().ShouldBe(200);
        completed.GetProperty("model").GetString().ShouldBe("scripted");

        // The model saw the tool result with catalog ids before writing the answer.
        var second = fixture.Factory.Chat.StreamRequests.Single(r => r.Messages[^1].Role == ChatRole.Tool && r.Messages.Any(m => m.Text == question));
        second.Messages[^1].Contents.OfType<FunctionResultContent>().Single().Result!.ToString()!.ShouldContain(interstellarId.ToString());

        // Reload: the history has the user message and the answer with its cards (tool rounds are not shown).
        var view = await client.GetFromJsonAsync<ConversationView>($"/api/chat/conversations/{conversation.Id}", Json, Ct);
        view!.Title.ShouldBe(question);
        view.Messages.Select(m => m.Role).ShouldBe([MessageRole.User, MessageRole.Assistant]);
        view.Messages[0].Text.ShouldBe(question);
        var answer = view.Messages[1];
        answer.Id.ShouldBe(messageId);
        answer.Text.ShouldBe("Вот что я нашёл.");
        answer.Movies.Select(m => m.Id).ShouldBe(cards.Select(c => c.Id));
        answer.PromptVersion.ShouldNotBeNull().ShouldStartWith("assistant-system.v");
        answer.Usage.ShouldBe(new UsageView(200, 20, 0));
        view.Usage.InputTokens.ShouldBe(200);

        var list = await client.GetFromJsonAsync<List<ConversationListItem>>("/api/chat/conversations", Json, Ct);
        list!.ShouldContain(c => c.Id == conversation.Id && c.Title == question);
    }

    [Fact]
    public async Task Conversations_belong_to_their_owner()
    {
        using var owner = CreateClient();
        using var stranger = CreateClient();
        var conversation = await CreateConversationAsync(owner);

        (await stranger.GetAsync($"/api/chat/conversations/{conversation.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new { text = "hi" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetFromJsonAsync<List<ConversationListItem>>("/api/chat/conversations", Json, Ct))!.ShouldNotContain(c => c.Id == conversation.Id);
    }

    [Fact]
    public async Task Invalid_input_is_a_400_before_any_stream()
    {
        using var client = CreateClient();
        var conversation = await CreateConversationAsync(client);

        (await client.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new { text = "   " }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new { text = new string('a', AssistantLimits.MaxMessageLength + 1) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/chat/conversations", new { locale = "de" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Answers_can_be_rated_and_the_rating_is_shown_in_the_history()
    {
        const string question = "Rate this answer (api test)";
        fixture.Factory.Chat.ScriptStream(question, [new TextContent("What kind of movie do you like?")]);
        using var client = CreateClient("en");
        using var stranger = CreateClient();
        var conversation = await CreateConversationAsync(client);
        using (var response = await client.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new { text = question }, Ct))
        {
            ParseEvents(await response.Content.ReadAsStringAsync(Ct))[^1].Name.ShouldBe("message.completed");
        }

        var view = await client.GetFromJsonAsync<ConversationView>($"/api/chat/conversations/{conversation.Id}", Json, Ct);
        var (userMessage, answer) = (view!.Messages[0], view.Messages[1]);
        answer.Feedback.ShouldBeNull();

        (await client.PostAsJsonAsync($"/api/chat/messages/{answer.Id}/feedback", new { rating = -1, comment = "too vague" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync($"/api/chat/messages/{answer.Id}/feedback", new { rating = 1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync($"/api/chat/messages/{answer.Id}/feedback", new { rating = 0 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/chat/messages/{userMessage.Id}/feedback", new { rating = 1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await stranger.PostAsJsonAsync($"/api/chat/messages/{answer.Id}/feedback", new { rating = 1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        view = await client.GetFromJsonAsync<ConversationView>($"/api/chat/conversations/{conversation.Id}", Json, Ct);
        view!.Messages[1].Feedback.ShouldBe(new FeedbackView(1, null));
    }

    private HttpClient CreateClient(string? acceptLanguage = null)
    {
        // https: the owner cookie is Secure. Each client is a new owner.
        var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (acceptLanguage is not null)
        {
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(acceptLanguage);
        }

        return client;
    }

    private static async Task<ConversationListItem> CreateConversationAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/chat/conversations", content: null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ConversationListItem>(Json, Ct))!;
    }

    /// <summary>Parses a <c>text/event-stream</c> body: blocks separated by a blank line, <c>event:</c> and <c>data:</c> lines.</summary>
    internal static List<(string Name, JsonElement Data)> ParseEvents(string body)
    {
        var events = new List<(string, JsonElement)>();
        foreach (var block in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string? name = null;
            var data = new StringBuilder();
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    name = line["event:".Length..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    data.Append(line["data:".Length..].TrimStart());
                }
            }

            if (name is not null)
            {
                events.Add((name, JsonDocument.Parse(data.ToString()).RootElement.Clone()));
            }
        }

        return events;
    }
}
