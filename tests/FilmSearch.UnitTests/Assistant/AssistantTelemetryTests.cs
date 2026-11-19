using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FilmSearch.AI.Assistant;
using FilmSearch.AI.Assistant.Tools;
using FilmSearch.AI.Telemetry;
using FilmSearch.Core.Assistant;
using static FilmSearch.UnitTests.Assistant.ScriptedStreamingChatClient;

namespace FilmSearch.UnitTests.Assistant;

/// <summary>I18: assistant metrics, the owner's daily token budget (NFR-5), turn span usage and the content recording switch.</summary>
public sealed class AssistantTelemetryTests : IDisposable
{
    private readonly AssistantHarness _harness = new();
    private readonly HarnessMeters _meters;

    public AssistantTelemetryTests() => _meters = new HarnessMeters(_harness.MeterFactory);

    public void Dispose() => _meters.Dispose();

    [Fact]
    public async Task An_owner_over_the_daily_budget_is_refused_before_any_model_call()
    {
        _harness.Options.MaxConversationTokens = 1000;
        _harness.Options.MaxOwnerTokensPerDay = 5000;
        _harness.Store.OtherConversationTokens = 5000;
        var conversation = _harness.Store.Add("en", "local");

        var events = await _harness.SendAsync(conversation, "space");

        events.Single().ShouldBeOfType<ErrorEvent>().Code.ShouldBe(AssistantErrorCodes.DailyBudgetExceeded);
        _harness.Chat.StreamRequests.ShouldBeEmpty();
        _harness.Chat.Requests.ShouldBeEmpty();
        _harness.Store.Messages.ShouldBeEmpty();
        _meters.Of("assistant.turns").ShouldHaveSingleItem().Tag("outcome").ShouldBe(AssistantErrorCodes.DailyBudgetExceeded);
    }

    [Fact]
    public async Task Answers_of_the_last_24_hours_count_for_the_daily_budget()
    {
        _harness.Options.MaxOwnerTokensPerDay = 300_000;
        var old = _harness.Store.Add("en", "local");
        _harness.Store.Messages.Add(new ConversationMessage
        {
            ConversationId = old.Id,
            Role = MessageRole.Assistant,
            InputTokens = 400_000,
            OutputTokens = 0,
            CreatedAt = _harness.Time.GetUtcNow().AddHours(-25),
        });
        _harness.Chat.Answer(Text("Hello."));
        var conversation = _harness.Store.Add("en", "local");

        var events = await _harness.SendAsync(conversation, "hi");

        events[^1].ShouldBeOfType<MessageCompletedEvent>();
    }

    [Fact]
    public async Task A_turn_records_its_outcome_duration_tool_calls_and_iteration_limit()
    {
        _harness.Options.MaxToolIterations = 1;
        _harness.Search.Results = [AssistantHarness.Movie("Moon", 2009)];
        _harness.Chat.Answer(
            Call("c1", SearchMoviesTool.ToolName, new() { ["query"] = "space" }),
            Call("c2", "buy_everything", new()));
        _harness.Chat.Answer(Text("Try **Moon** (2009) or **Solaris**."));
        var conversation = _harness.Store.Add("en", "local");

        (await _harness.SendAsync(conversation, "space"))[^1].ShouldBeOfType<MessageCompletedEvent>();

        var turn = _meters.Of("assistant.turns").ShouldHaveSingleItem();
        turn.Tag("outcome").ShouldBe(AssistantMetrics.Answered);
        turn.Tag("prompt").ShouldNotBe("none");
        _meters.Of("assistant.turn.duration").ShouldHaveSingleItem();
        _meters.Of("assistant.tool_calls").Select(m => ((string?)m.Tag("tool"), (bool?)m.Tag("ok")))
            .ShouldBe([(SearchMoviesTool.ToolName, true), ("buy_everything", false)]);
        _meters.Of("assistant.iteration_limit_hits").ShouldHaveSingleItem();
        _meters.Of("assistant.grounding_violations").ShouldHaveSingleItem(); // Solaris came from no tool
    }

    [Fact]
    public async Task A_provider_failure_counts_as_its_error_code()
    {
        _harness.Chat.Fail(new HttpRequestException("connection refused"));
        var conversation = _harness.Store.Add("en", "local");

        await _harness.SendAsync(conversation, "space");

        _meters.Of("assistant.turns").ShouldHaveSingleItem().Tag("outcome").ShouldBe(AssistantErrorCodes.ProviderUnavailable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tool_arguments_results_and_turn_texts_are_on_spans_only_when_sensitive_data_is_on(bool enabled)
    {
        _harness.Ai.EnableSensitiveData = enabled;
        var callId = $"sensitive-{Guid.NewGuid():N}";
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AiMetrics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        _harness.Search.Results = [AssistantHarness.Movie("Moon", 2009)];
        _harness.Chat.Answer(Call(callId, SearchMoviesTool.ToolName, new() { ["query"] = "lonely astronaut" }));
        _harness.Chat.Answer(Text("Try **Moon** (2009)."));
        var conversation = _harness.Store.Add("en", "local");

        await _harness.SendAsync(conversation, "a lonely astronaut");

        var tool = spans.Single(s => (string?)s.GetTagItem("gen_ai.tool.call.id") == callId);
        var turn = spans.Single(s => s.OperationName == "invoke_agent assistant" && (string?)s.GetTagItem(AssistantChat.ConversationTag) == conversation.Id.ToString());
        tool.GetTagItem("filmsearch.tool.ok").ShouldBe(true);
        turn.GetTagItem("gen_ai.usage.input_tokens").ShouldBe(200L);
        turn.GetTagItem("gen_ai.usage.output_tokens").ShouldBe(20L);
        turn.GetTagItem(AiMetrics.CostTag).ShouldNotBeNull();
        turn.GetTagItem("langfuse.session.id").ShouldBe(conversation.Id.ToString());
        if (enabled)
        {
            ((string)tool.GetTagItem("gen_ai.tool.call.arguments")!).ShouldContain("lonely astronaut");
            ((string)tool.GetTagItem("gen_ai.tool.call.result")!).ShouldContain("Moon");
            turn.GetTagItem("langfuse.trace.input").ShouldBe("a lonely astronaut");
            turn.GetTagItem("langfuse.trace.output").ShouldBe("Try **Moon** (2009).");
        }
        else
        {
            tool.GetTagItem("gen_ai.tool.call.arguments").ShouldBeNull();
            tool.GetTagItem("gen_ai.tool.call.result").ShouldBeNull();
            turn.GetTagItem("langfuse.trace.input").ShouldBeNull();
            turn.GetTagItem("langfuse.trace.output").ShouldBeNull();
        }

        // The answer keeps the trace id of its turn: the way from a rating to the trace.
        _harness.Store.Messages.Single(m => m.Role == MessageRole.Assistant && m.ToolCallsJson is null).TraceId.ShouldBe(turn.TraceId.ToHexString());
    }

    /// <summary>Measurements of the meters of one harness (tests run in parallel and record the same instruments).</summary>
    private sealed class HarnessMeters : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string Instrument, IReadOnlyDictionary<string, object?> Tags)> _measurements = new();

        public HarnessMeters(TestMeterFactory factory)
        {
            _listener.InstrumentPublished = (instrument, l) =>
            {
                if (factory.Owns(instrument.Meter))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((i, _, tags, _) => Add(i, tags));
            _listener.SetMeasurementEventCallback<double>((i, _, tags, _) => Add(i, tags));
            _listener.Start();
        }

        public IReadOnlyList<Measured> Of(string instrument) =>
            [.. _measurements.Where(m => m.Instrument == instrument).Select(m => new Measured(m.Tags))];

        public void Dispose() => _listener.Dispose();

        private void Add(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
            _measurements.Enqueue((instrument.Name, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));
    }

    private sealed record Measured(IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string name) => Tags.GetValueOrDefault(name);
    }
}
