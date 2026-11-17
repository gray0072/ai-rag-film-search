using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using FilmSearch.AI.Providers;
using FilmSearch.AI.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FilmSearch.UnitTests.AI;

public sealed class AiProviderPipelineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unkeyed_chat_client_uses_the_active_profile_and_its_defaults()
    {
        var handler = new FakeProviderHandler();
        using var host = AiTestHost.Build(handler);

        var response = await host.Services.GetRequiredService<IChatClient>().GetResponseAsync("Hi", cancellationToken: Ct);

        response.Text.ShouldBe("Hello there");
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.ToString().ShouldBe("http://local.test/v1/chat/completions");
        request.Authorization.ShouldBe("Bearer lm-studio");
        using var body = JsonDocument.Parse(request.Body);
        body.RootElement.GetProperty("model").GetString().ShouldBe("local-model");
        body.RootElement.GetProperty("temperature").GetDouble().ShouldBe(0.3, 0.0001);
        body.RootElement.GetProperty("max_completion_tokens").GetInt32().ShouldBe(256);
        body.RootElement.GetProperty("reasoning_effort").GetString().ShouldBe("none");
    }

    [Fact]
    public async Task Call_options_override_profile_defaults()
    {
        var handler = new FakeProviderHandler();
        using var host = AiTestHost.Build(handler);

        await host.Services.GetRequiredService<IChatClient>()
            .GetResponseAsync("Hi", new ChatOptions { Temperature = 0.9f, MaxOutputTokens = 10 }, Ct);

        using var body = JsonDocument.Parse(handler.Requests.Single().Body);
        body.RootElement.GetProperty("temperature").GetDouble().ShouldBe(0.9, 0.0001);
        body.RootElement.GetProperty("max_completion_tokens").GetInt32().ShouldBe(10);
    }

    [Fact]
    public async Task Switching_the_active_profile_in_config_changes_provider_and_model()
    {
        var handler = new FakeProviderHandler();
        using var host = AiTestHost.Build(handler, new() { ["AI:ActiveChatProfile"] = "openrouter" });

        var response = await host.Services.GetRequiredService<IChatClient>().GetResponseAsync("Hi", cancellationToken: Ct);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.ToString().ShouldBe("https://router.test/api/v1/chat/completions");
        request.Authorization.ShouldBe("Bearer secret-key");
        JsonDocument.Parse(request.Body).RootElement.GetProperty("model").GetString().ShouldBe("vendor/cheap-model");

        // 100 input tokens × $1/M + 50 output tokens × $2/M
        var usage = response.AdditionalProperties![AiUsage.PropertyKey].ShouldBeOfType<AiUsage>();
        usage.ShouldBe(new AiUsage("openrouter", "vendor/cheap-model", "chat", 100, 50, 0.0002m));
    }

    [Fact]
    public async Task Named_profiles_are_keyed_services()
    {
        var handler = new FakeProviderHandler();
        using var host = AiTestHost.Build(handler);

        var openRouter = host.Services.GetRequiredKeyedService<IChatClient>("openrouter");
        var active = host.Services.GetRequiredService<IChatClient>();

        active.ShouldBeSameAs(host.Services.GetRequiredKeyedService<IChatClient>("local"));
        openRouter.ShouldNotBeSameAs(active);
        openRouter.GetService<ChatClientMetadata>()!.DefaultModelId.ShouldBe("vendor/cheap-model");
    }

    [Fact]
    public async Task Model_without_pricing_has_usage_but_no_cost()
    {
        var handler = new FakeProviderHandler();
        using var host = AiTestHost.Build(handler);

        var response = await host.Services.GetRequiredService<IChatClient>().GetResponseAsync("Hi", cancellationToken: Ct);

        var usage = response.AdditionalProperties![AiUsage.PropertyKey].ShouldBeOfType<AiUsage>();
        usage.InputTokens.ShouldBe(100);
        usage.OutputTokens.ShouldBe(50);
        usage.EstimatedCostUsd.ShouldBeNull();
    }

    [Fact]
    public async Task Streaming_response_records_token_metrics_from_the_last_chunk()
    {
        var handler = new FakeProviderHandler { Respond = FakeProviderHandler.StreamResponse };
        using var host = AiTestHost.Build(handler, new() { ["AI:ActiveChatProfile"] = "openrouter" });
        var chat = host.Services.GetRequiredService<IChatClient>();
        using var tokens = new MeasurementRecorder<long>("ai.tokens");
        using var cost = new MeasurementRecorder<double>("ai.cost_usd");

        var text = "";
        await foreach (var update in chat.GetStreamingResponseAsync("Hi", cancellationToken: Ct))
        {
            text += update.Text;
        }

        text.ShouldBe("Hello");
        tokens.Values.ShouldContain(m => m.Value == 100 && m.Tags["direction"] as string == "input" && m.Tags["profile"] as string == "openrouter");
        tokens.Values.ShouldContain(m => m.Value == 50 && m.Tags["direction"] as string == "output" && m.Tags["operation"] as string == "chat_stream");
        cost.Values.Where(m => m.Tags["operation"] as string == "chat_stream").ShouldHaveSingleItem().Value.ShouldBe(0.0002, 1e-9);
    }

    [Fact]
    public async Task Chat_call_produces_a_GenAI_span_with_model_tokens_and_profile()
    {
        var handler = new FakeProviderHandler();
        using var host = AiTestHost.Build(handler);
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AiMetrics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await host.Services.GetRequiredService<IChatClient>().GetResponseAsync("Hi", cancellationToken: Ct);

        var span = spans.ShouldHaveSingleItem();
        span.GetTagItem("gen_ai.request.model").ShouldBe("local-model");
        span.GetTagItem("gen_ai.usage.input_tokens").ShouldBe(100);
        span.GetTagItem("gen_ai.usage.output_tokens").ShouldBe(50);
        span.GetTagItem(AiMetrics.ProfileTag).ShouldBe("local");
    }

    [Fact]
    public async Task Embedding_generator_uses_the_active_embedding_profile()
    {
        var handler = new FakeProviderHandler();
        using var host = AiTestHost.Build(handler);
        var generator = host.Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        var embeddings = await generator.GenerateAsync(["a movie about dreams"], cancellationToken: Ct);

        embeddings.ShouldHaveSingleItem().Vector.ToArray().ShouldBe([0.1f, 0.2f, 0.3f]);
        handler.Requests.Single().Uri.ToString().ShouldBe("http://local.test/v1/embeddings");
        JsonDocument.Parse(handler.Requests.Single().Body).RootElement.GetProperty("model").GetString().ShouldBe("embedding-model");
        var usage = embeddings.AdditionalProperties![AiUsage.PropertyKey].ShouldBeOfType<AiUsage>();
        usage.InputTokens.ShouldBe(5);
    }

    [Fact]
    public async Task Provider_error_surfaces_as_exception_after_configured_retries()
    {
        var handler = new FakeProviderHandler
        {
            Respond = _ => FakeProviderHandler.Json("""{"error":{"message":"overloaded"}}""", System.Net.HttpStatusCode.ServiceUnavailable),
        };
        using var host = AiTestHost.Build(handler, new() { ["AI:ChatProfiles:local:MaxRetries"] = "1" });

        await Should.ThrowAsync<System.ClientModel.ClientResultException>(() =>
            host.Services.GetRequiredService<IChatClient>().GetResponseAsync("Hi", cancellationToken: Ct));

        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public void Active_profile_without_api_key_fails_options_validation()
    {
        using var host = AiTestHost.Build(new FakeProviderHandler(), new()
        {
            ["AI:ActiveChatProfile"] = "openrouter",
            ["AI:ChatProfiles:openrouter:ApiKey"] = "",
        });

        var exception = Should.Throw<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<AiOptions>>().Value);
        exception.Message.ShouldContain("AI:ChatProfiles:openrouter:ApiKey is required");
    }
}

/// <summary>Collects measurements of one FilmSearch instrument while it is alive.</summary>
internal sealed class MeasurementRecorder<T> : IDisposable
    where T : struct
{
    private readonly MeterListener _listener = new();

    public MeasurementRecorder(string instrument)
    {
        _listener.InstrumentPublished = (i, listener) =>
        {
            if (i.Meter.Name == AiMetrics.MeterName && i.Name == instrument)
            {
                listener.EnableMeasurementEvents(i);
            }
        };
        _listener.SetMeasurementEventCallback<T>((_, value, tags, _) =>
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            lock (Values)
            {
                Values.Add((value, copy));
            }
        });
        _listener.Start();
    }

    public List<(T Value, Dictionary<string, object?> Tags)> Values { get; } = [];

    public void Dispose() => _listener.Dispose();
}
