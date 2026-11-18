using System.Text.Json;
using FilmSearch.AI.Assistant;
using FilmSearch.AI.Assistant.Tools;
using FilmSearch.Core.Assistant;
using Microsoft.Extensions.AI;
using static FilmSearch.UnitTests.Assistant.ScriptedStreamingChatClient;

namespace FilmSearch.UnitTests.Assistant;

/// <summary>The agent loop of a turn with a scripted model (spec 06 §2): tool call → result → answer, limits, failures, history.</summary>
public sealed class AssistantChatTests
{
    private readonly AssistantHarness _harness = new();

    [Fact]
    public async Task Tool_call_then_answer_streams_events_in_order_and_stores_the_turn()
    {
        var interstellar = AssistantHarness.Movie("Interstellar");
        var gravity = AssistantHarness.Movie("Gravity", 2013);
        _harness.Search.Results = [interstellar, gravity];
        _harness.Chat.Answer(Call("call-1", SearchMoviesTool.ToolName, new() { ["query"] = "space", ["genresInclude"] = new List<string> { "science-fiction" } }));
        _harness.Chat.Answer(Text("Here are "), Text("two movies."));
        var conversation = _harness.Store.Add("en", "local");

        var events = await _harness.SendAsync(conversation, "Recommend something about space");

        events.Select(e => e.EventType).ShouldBe(
        [
            "message.started", "tool.started", "tool.completed", "movies", "message.delta", "message.delta", "message.completed",
        ]);
        var started = events.OfType<MessageStartedEvent>().Single();
        events.OfType<ToolStartedEvent>().Single().ArgsSummary.ShouldContain("space");
        events.OfType<ToolCompletedEvent>().Single().Ok.ShouldBeTrue();
        events.OfType<MoviesEvent>().Single().Items.Select(c => c.Title).ShouldBe(["Interstellar", "Gravity"]);
        var completed = events.OfType<MessageCompletedEvent>().Single();
        completed.MessageId.ShouldBe(started.MessageId);
        completed.Usage.ShouldBe(new TokenUsage(200, 20));
        completed.Model.ShouldBe(AssistantHarness.Model);

        // The search got the filters from the tool arguments and the turn's locale.
        var query = _harness.Search.Queries.Single();
        query.Text.ShouldBe("space");
        query.Locale.ShouldBe("en");
        query.Filter.GenreSlugs.ShouldBe(["science-fiction"]);

        // Second model call: the tool result with catalog ids follows the assistant's call.
        var second = _harness.Chat.StreamRequests[1].Messages;
        second[0].Role.ShouldBe(ChatRole.System);
        second[0].Text.ShouldNotContain("{{");
        second[^2].Contents.OfType<FunctionCallContent>().Single().CallId.ShouldBe("call-1");
        var result = second[^1].Contents.OfType<FunctionResultContent>().Single();
        result.CallId.ShouldBe("call-1");
        result.Result!.ToString()!.ShouldContain(interstellar.Id.ToString());

        // Stored: user message, tool round (call + result), answer with cards, usage, cost and prompt version.
        var stored = _harness.Store.Messages.OrderBy(m => m.Seq).ToList();
        stored.Select(m => m.Role).ShouldBe([MessageRole.User, MessageRole.Assistant, MessageRole.Tool, MessageRole.Assistant]);
        stored.Select(m => m.Seq).ShouldBe([1, 2, 3, 4]);
        stored[1].ToolCallsJson.ShouldNotBeNull().ShouldContain("call-1");
        stored[2].ToolCallId.ShouldBe("call-1");
        var answer = stored[3];
        answer.Id.ShouldBe(started.MessageId);
        answer.Content.ShouldBe("Here are two movies.");
        answer.MovieIds.ShouldBe([interstellar.Id, gravity.Id]);
        answer.InputTokens.ShouldBe(200);
        answer.OutputTokens.ShouldBe(20);
        answer.EstimatedCostUsd.ShouldBe(0.0004m); // 200 × $1/M + 20 × $10/M
        answer.PromptVersion.ShouldStartWith("assistant-system.v");
        answer.Model.ShouldBe(AssistantHarness.Model);

        conversation.Title.ShouldBe("Recommend something about space");
        conversation.TotalInputTokens.ShouldBe(200);
        conversation.EstimatedCostUsd.ShouldBe(0.0004m);
        _harness.Locks.TryEnter(conversation.Id).ShouldBeTrue(); // released after the turn
    }

    [Fact]
    public async Task Tools_are_declared_with_a_schema_and_genre_enum()
    {
        _harness.Chat.Answer(Text("Hi! What kind of movie would you like?"));
        var conversation = _harness.Store.Add("en", "local");

        await _harness.SendAsync(conversation, "hello");

        var tool = _harness.Chat.StreamRequests.Single().Options!.Tools!.OfType<AIFunctionDeclaration>().Single();
        tool.Name.ShouldBe("search_movies");
        var schema = tool.JsonSchema;
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldBe(["query"]);
        schema.GetProperty("properties").GetProperty("genresExclude").GetProperty("items").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ShouldBe(["comedy", "horror", "science-fiction"]);
    }

    [Fact]
    public async Task Invalid_arguments_and_unknown_tools_go_back_to_the_model_as_errors()
    {
        _harness.Chat.Answer(
            Call("call-1", SearchMoviesTool.ToolName, new() { ["limit"] = 3 }),
            Call("call-2", "buy_everything", new()));
        _harness.Chat.Answer(Text("Sorry, what are you looking for?"));
        var conversation = _harness.Store.Add("en", "local");

        var events = await _harness.SendAsync(conversation, "something");

        events.OfType<ToolCompletedEvent>().Select(e => e.Ok).ShouldBe([false, false]);
        events.OfType<MoviesEvent>().ShouldBeEmpty();
        var results = _harness.Chat.StreamRequests[1].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
        results[0].Result!.ToString()!.ShouldContain("`query` is required");
        results[1].Result!.ToString()!.ShouldContain("Unknown tool 'buy_everything'");
        _harness.Search.Queries.ShouldBeEmpty();
        events[^1].ShouldBeOfType<MessageCompletedEvent>();
    }

    [Fact]
    public async Task After_the_tool_limit_one_call_without_tools_writes_the_answer()
    {
        _harness.Options.MaxToolIterations = 2;
        _harness.Search.Results = [AssistantHarness.Movie("Moon", 2009)];
        _harness.Chat.Answer(Call("c1", SearchMoviesTool.ToolName, new() { ["query"] = "space" }));
        _harness.Chat.Answer(Call("c2", SearchMoviesTool.ToolName, new() { ["query"] = "moon" }));
        _harness.Chat.Answer(Text("Try **Moon** (2009)."));
        var conversation = _harness.Store.Add("en", "local");

        var events = await _harness.SendAsync(conversation, "space");

        _harness.Chat.StreamRequests.Count.ShouldBe(3);
        _harness.Chat.StreamRequests[2].Options!.Tools.ShouldBeNull();
        _harness.Chat.StreamRequests[2].Messages[^1].Text.ShouldBe(AgentLoop.IterationLimitNote);
        events.OfType<MoviesEvent>().Count().ShouldBe(1); // the same movie twice is shown once
        _harness.Store.Messages.Single(m => m.Role == MessageRole.Assistant && m.ToolCallsJson is null).Content.ShouldBe("Try **Moon** (2009).");
    }

    [Fact]
    public async Task Provider_failure_ends_the_stream_with_an_error_and_keeps_the_user_message()
    {
        _harness.Chat.Fail(new HttpRequestException("connection refused"));
        var conversation = _harness.Store.Add("en", "local");

        var events = await _harness.SendAsync(conversation, "space movies");

        events.Select(e => e.EventType).ShouldBe(["message.started", "error"]);
        events.OfType<ErrorEvent>().Single().Code.ShouldBe(AssistantErrorCodes.ProviderUnavailable);
        _harness.Store.Messages.Single().Role.ShouldBe(MessageRole.User);
        _harness.Locks.TryEnter(conversation.Id).ShouldBeTrue();
    }

    [Fact]
    public async Task A_second_turn_of_a_busy_conversation_is_refused()
    {
        var conversation = _harness.Store.Add("en", "local");
        _harness.Locks.TryEnter(conversation.Id);

        var events = await _harness.SendAsync(conversation, "space");

        events.Single().ShouldBeOfType<ErrorEvent>().Code.ShouldBe(AssistantErrorCodes.ConversationBusy);
        _harness.Store.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_conversation_over_its_token_budget_is_refused()
    {
        _harness.Options.MaxConversationTokens = 1000;
        var conversation = _harness.Store.Add("en", "local");
        conversation.AddUsage(900, 100, null);

        var events = await _harness.SendAsync(conversation, "space");

        events.Single().ShouldBeOfType<ErrorEvent>().Code.ShouldBe(AssistantErrorCodes.BudgetExceeded);
        _harness.Chat.StreamRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Next_turn_sends_earlier_turns_with_compact_tool_results()
    {
        var interstellar = AssistantHarness.Movie("Interstellar", overview: new string('x', 280));
        _harness.Search.Results = [interstellar];
        _harness.Chat.Answer(Call("call-1", SearchMoviesTool.ToolName, new() { ["query"] = "space" }));
        _harness.Chat.Answer(Text("Watch **Interstellar**."));
        _harness.Chat.Answer(Text("It is 169 minutes long."));
        var conversation = _harness.Store.Add("en", "local");

        await _harness.SendAsync(conversation, "space");
        await _harness.SendAsync(conversation, "How long is it?");

        var messages = _harness.Chat.StreamRequests[2].Messages;
        messages.Select(m => m.Role).ShouldBe([ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant, ChatRole.User]);
        var earlierResult = messages[3].Contents.OfType<FunctionResultContent>().Single().Result!.ToString()!;
        earlierResult.ShouldContain(interstellar.Id.ToString());
        earlierResult.ShouldNotContain("xxxx"); // overview cut from earlier turns
        messages[4].Text.ShouldBe("Watch **Interstellar**.");
        messages[5].Text.ShouldBe("How long is it?");
        _harness.Store.Messages.Max(m => m.Seq).ShouldBe(6);
    }

    [Fact]
    public async Task Turns_beyond_the_window_are_folded_into_the_rolling_summary()
    {
        _harness.Options.HistoryMaxMessages = 2;
        _harness.Chat.Answer(Text("Answer."));
        _harness.Chat.SummaryAnswer = "The user asked about space and comedy.";
        var conversation = _harness.Store.Add("en", "local");

        await _harness.SendAsync(conversation, "first");
        await _harness.SendAsync(conversation, "second");
        await _harness.SendAsync(conversation, "third");

        // Third turn: "first" + answer no longer fit the window of 2 messages → summarized.
        var summaryRequest = _harness.Chat.Requests.Single();
        summaryRequest[1].Text.ShouldContain("User: first");
        summaryRequest[1].Text.ShouldNotContain("second");
        conversation.Summary.ShouldBe("The user asked about space and comedy.");
        conversation.SummarizedThroughSeq.ShouldBe(2);

        var third = _harness.Chat.StreamRequests[2].Messages;
        third[0].Text.ShouldContain("<summary>\nThe user asked about space and comedy.\n</summary>");
        third.Skip(1).Select(m => m.Text).ShouldBe(["second", "Answer.", "third"]);
        conversation.TotalInputTokens.ShouldBe((3 * 100) + 50);
    }

    [Fact]
    public async Task Unanswered_user_messages_are_left_out_of_the_context()
    {
        _harness.Chat.Fail(new HttpRequestException("down"));
        _harness.Chat.Answer(Text("Back again."));
        var conversation = _harness.Store.Add("en", "local");

        await _harness.SendAsync(conversation, "lost");
        await _harness.SendAsync(conversation, "again");

        _harness.Chat.StreamRequests[1].Messages.Select(m => m.Text).Skip(1).ShouldBe(["again"]);
    }

    [Fact]
    public async Task Tool_spans_are_children_of_the_turn_span()
    {
        var spans = new System.Collections.Concurrent.ConcurrentQueue<System.Diagnostics.Activity>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == FilmSearch.AI.Telemetry.AiMetrics.SourceName,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        _harness.Search.Results = [AssistantHarness.Movie("Moon", 2009)];
        _harness.Chat.Answer(Call("trace-1", SearchMoviesTool.ToolName, new() { ["query"] = "trace test" }));
        _harness.Chat.Answer(Text("Moon."));
        var conversation = _harness.Store.Add("en", "local");

        await _harness.SendAsync(conversation, "trace test");

        var turn = spans.Single(s => s.OperationName == "invoke_agent assistant" && (string?)s.GetTagItem(AssistantChat.ConversationTag) == conversation.Id.ToString());
        var tool = spans.Single(s => s.OperationName == "execute_tool search_movies" && (string?)s.GetTagItem("gen_ai.tool.call.id") == "trace-1");
        tool.ParentSpanId.ShouldBe(turn.SpanId);
        turn.GetTagItem(AssistantChat.PromptTag).ShouldNotBeNull();
    }

    [Fact]
    public void Stored_tool_calls_round_trip()
    {
        var json = ConversationHistory.SerializeToolCalls([new FunctionCallContent("c1", "search_movies", new Dictionary<string, object?> { ["query"] = "space", ["limit"] = 3 })]);

        var call = ConversationHistory.ParseToolCalls(json).Single();

        call.CallId.ShouldBe("c1");
        call.Name.ShouldBe("search_movies");
        ((JsonElement)call.Arguments!["query"]!).GetString().ShouldBe("space");
        ((JsonElement)call.Arguments["limit"]!).GetInt32().ShouldBe(3);
    }
}
