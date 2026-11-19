using System.Net;
using System.Net.Http.Json;
using FilmSearch.AI.Telemetry;
using FilmSearch.Core.Assistant;
using FilmSearch.Infrastructure.Persistence;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.IntegrationTests;

/// <summary>
/// I18 (spec 10 §3, NFR-4, NFR-5): the owner's daily token budget, the trace id stored with an answer, feedback as an
/// online signal and a Langfuse score, and the spans and metrics of a search request.
/// </summary>
public sealed class ObservabilityApiTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.EnsureEmbeddingsAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task The_daily_budget_of_an_owner_covers_all_conversations_and_refuses_without_a_model_call()
    {
        const string first = "Space movies please (daily budget test)";
        const string second = "One more question (daily budget test)";
        fixture.Factory.Chat.ScriptStream(first, [new TextContent("What year do you like?")]);
        using var client = CreateClient();
        using var stranger = CreateClient();

        var used = await CreateConversationAsync(client);
        (await SendAsync(client, used.Id, first))[^1].Name.ShouldBe("message.completed");

        // As if the owner had used the whole day's budget in that conversation.
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Set<ConversationMessage>()
                .Where(m => m.ConversationId == used.Id && m.Role == MessageRole.Assistant)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.InputTokens, 1_000_000), Ct)).ShouldBe(1);
        }

        var fresh = await CreateConversationAsync(client);
        var events = await SendAsync(client, fresh.Id, second);

        events.Select(e => e.Name).ShouldBe(["error"]);
        events[0].Data.GetProperty("code").GetString().ShouldBe(AssistantErrorCodes.DailyBudgetExceeded);
        fixture.Factory.Chat.StreamRequests.ShouldNotContain(r => r.Messages.Any(m => m.Text == second));

        // Another owner is not affected: its turn reaches the (unscripted) model.
        var other = await CreateConversationAsync(stranger);
        (await SendAsync(stranger, other.Id, second))[0].Name.ShouldBe("message.started");
    }

    [Fact]
    public async Task An_answer_stores_its_trace_id_and_its_rating_becomes_a_metric_and_a_score_of_that_trace()
    {
        const string question = "Something funny (trace id test)";
        fixture.Factory.Chat.ScriptStream(question, [new TextContent("A comedy, then.")]);
        using var recorder = new MeterRecorder(fixture.Factory.Services, "assistant.turns", "assistant.feedback");
        using var spans = new SpanRecorder();
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("traceparent", spans.TraceParent);

        var conversation = await CreateConversationAsync(client);
        var events = await SendAsync(client, conversation.Id, question);
        var answerId = events[0].Data.GetProperty("messageId").GetGuid();

        // The turn span is in the request's trace, and that trace id is stored with the answer.
        var turn = spans.Spans.Where(s => s.OperationName == "invoke_agent assistant").ShouldHaveSingleItem();
        turn.GetTagItem("gen_ai.conversation.id").ShouldBe(conversation.Id.ToString());
        turn.GetTagItem("gen_ai.usage.input_tokens").ShouldBe(100L);
        turn.GetTagItem("langfuse.session.id").ShouldBe(conversation.Id.ToString());
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Set<ConversationMessage>().Where(m => m.Id == answerId).Select(m => m.TraceId).SingleAsync(Ct)).ShouldBe(spans.TraceId.ToHexString());
        }

        recorder.Of("assistant.turns").ShouldContain(m => (string?)m.Tag("outcome") == "answered");

        (await client.PostAsJsonAsync($"/api/chat/messages/{answerId}/feedback", new { rating = -1, comment = "not funny" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        recorder.Of("assistant.feedback").ShouldHaveSingleItem().Tag("rating").ShouldBe("down");
        var score = fixture.Factory.FeedbackScores.Scores.Where(s => s.MessageId == answerId).ShouldHaveSingleItem();
        score.ShouldBe(new FeedbackScore(spans.TraceId.ToHexString(), answerId, -1, "not funny"));
    }

    [Fact]
    public async Task A_hybrid_search_has_a_span_per_stage_and_is_counted_by_mode()
    {
        using var recorder = new MeterRecorder(fixture.Factory.Services, "search.requests", "search.latency");
        using var spans = new SpanRecorder();
        using var client = fixture.CreateClient("en");
        client.DefaultRequestHeaders.Add("traceparent", spans.TraceParent);

        using var response = await client.PostAsJsonAsync("/api/search", new { query = "space travel wormhole", mode = "hybrid" }, CatalogApiFixture.Json, Ct);
        response.EnsureSuccessStatusCode();

        var search = spans.Spans.Where(s => s.OperationName == "search hybrid").ShouldHaveSingleItem();
        search.Source.Name.ShouldBe("FilmSearch");
        search.GetTagItem("filmsearch.search.degraded").ShouldBe(false);
        ((int)search.GetTagItem("filmsearch.search.results")!).ShouldBeGreaterThan(0);
        var stages = spans.Spans.Where(s => s.Parent == search).Select(s => s.OperationName).ToList();
        stages.ShouldContain("search.keyword");
        stages.ShouldContain("search.embed_query");
        stages.ShouldContain("search.vector");
        stages.ShouldContain("search.fusion");

        var request = recorder.Of("search.requests").ShouldHaveSingleItem();
        request.Tag("mode").ShouldBe("hybrid");
        request.Tag("degraded").ShouldBe(false);
        recorder.Of("search.latency").ShouldHaveSingleItem().Value.ShouldBeGreaterThan(0);
    }

    private HttpClient CreateClient() =>
        // https: the owner cookie is Secure. Each client is a new owner.
        fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<ConversationListItem> CreateConversationAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/chat/conversations", content: null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ConversationListItem>(CatalogApiFixture.Json, Ct))!;
    }

    private static async Task<List<(string Name, System.Text.Json.JsonElement Data)>> SendAsync(HttpClient client, Guid conversationId, string text)
    {
        using var response = await client.PostAsJsonAsync($"/api/chat/conversations/{conversationId}/messages", new { text }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return AssistantApiTests.ParseEvents(await response.Content.ReadAsStringAsync(Ct));
    }
}
