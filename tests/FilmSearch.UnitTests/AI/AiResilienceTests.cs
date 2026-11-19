using System.Diagnostics.Metrics;
using System.Net;
using FilmSearch.AI.Providers;
using FilmSearch.UnitTests.Assistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.UnitTests.AI;

/// <summary>
/// Chaos tests of the provider pipeline (I19) through the real registration: the per-profile HTTP resilience pipeline
/// (retry only for safe cases, circuit breaker, attempt timeout) and the fallback chat profile, against a fake provider
/// that refuses connections, fails or hangs.
/// </summary>
public sealed class AiResilienceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool IsChat(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal);

    [Fact]
    public async Task Refused_connection_and_503_are_retried_with_backoff()
    {
        var attempt = 0;
        var handler = new FakeProviderHandler
        {
            Respond = request => ++attempt switch
            {
                1 => throw new HttpRequestException("Connection refused (local.test:80)"),
                2 => FakeProviderHandler.Json("""{"error":{"message":"loading model"}}""", HttpStatusCode.ServiceUnavailable),
                _ => FakeProviderHandler.DefaultResponse(request),
            },
        };
        using var host = AiTestHost.Build(handler, new() { ["AI:ChatProfiles:local:MaxRetries"] = "2" });

        var response = await host.Services.GetRequiredService<IChatClient>().GetResponseAsync("Hi", cancellationToken: Ct);

        response.Text.ShouldBe("Hello there");
        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Server_error_500_is_not_retried()
    {
        var handler = new FakeProviderHandler { Respond = _ => FakeProviderHandler.Json("""{"error":{"message":"bug"}}""", HttpStatusCode.InternalServerError) };
        using var host = AiTestHost.Build(handler, new() { ["AI:ChatProfiles:local:MaxRetries"] = "2" });

        var error = await Should.ThrowAsync<Exception>(() => host.Services.GetRequiredService<IChatClient>().GetResponseAsync("Hi", cancellationToken: Ct));

        AiProviderErrors.IsProviderFailure(error, Ct).ShouldBeTrue();
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Timeout_is_a_provider_failure_and_is_not_retried()
    {
        // A local model that is still generating: a second request would only queue behind the first one.
        var handler = new FakeProviderHandler { Delay = TimeSpan.FromSeconds(30) };
        using var host = AiTestHost.Build(handler, new()
        {
            ["AI:ChatProfiles:local:MaxRetries"] = "2",
            ["AI:ChatProfiles:local:TimeoutSeconds"] = "1",
        });

        var error = await Should.ThrowAsync<Exception>(() => host.Services.GetRequiredService<IChatClient>().GetResponseAsync("Hi", cancellationToken: Ct));

        AiProviderErrors.IsProviderFailure(error, Ct).ShouldBeTrue();
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Open_circuit_fails_calls_without_a_request_and_keeps_other_profiles_working()
    {
        var handler = new FakeProviderHandler
        {
            Respond = request => IsChat(request)
                ? FakeProviderHandler.Json("""{"error":{"message":"down"}}""", HttpStatusCode.BadGateway)
                : FakeProviderHandler.DefaultResponse(request),
        };
        using var host = AiTestHost.Build(handler);
        var chat = host.Services.GetRequiredService<IChatClient>();

        for (var i = 0; i < AiHttpResilience.MinimumThroughput; i++)
        {
            await Should.ThrowAsync<Exception>(() => chat.GetResponseAsync("Hi", cancellationToken: Ct));
        }

        var rejected = await Should.ThrowAsync<Exception>(() => chat.GetResponseAsync("Hi", cancellationToken: Ct));

        AiProviderErrors.IsProviderFailure(rejected, Ct).ShouldBeTrue();
        handler.Requests.Count(r => r.Uri.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal)).ShouldBe(AiHttpResilience.MinimumThroughput);

        // The embedding profile on the same server has its own breaker.
        var embeddings = await host.Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>().GenerateAsync(["x"], cancellationToken: Ct);
        embeddings.ShouldHaveSingleItem();

        // So has the other chat profile: its call reaches the provider (and gets the fake's 502).
        await Should.ThrowAsync<Exception>(() => host.Services.GetRequiredKeyedService<IChatClient>("openrouter").GetResponseAsync("Hi", cancellationToken: Ct));
        handler.Requests.ShouldContain(r => r.Uri.Host == "router.test");
    }

    [Fact]
    public async Task Fallback_profile_answers_when_the_active_profile_is_down()
    {
        using var meters = new TestMeterFactory();
        using var fallbacks = new OwnedMeasurements(meters, "ai.fallbacks");
        var handler = new FakeProviderHandler
        {
            Respond = request => request.RequestUri!.Host == "local.test"
                ? throw new HttpRequestException("Connection refused (local.test:80)")
                : FakeProviderHandler.DefaultResponse(request),
        };
        using var host = AiTestHost.Build(
            handler,
            new() { ["AI:FallbackChatProfile"] = "openrouter" },
            services => services.AddSingleton<IMeterFactory>(meters));

        var response = await host.Services.GetRequiredService<IChatClient>().GetResponseAsync("Hi", cancellationToken: Ct);

        response.Text.ShouldBe("Hello there");
        FallbackChatClient.FallbackProfileOf(response.AdditionalProperties).ShouldBe("openrouter");
        handler.Requests.Select(r => r.Uri.Host).ShouldBe(["local.test", "router.test"]);
        var measurement = fallbacks.Values.ShouldHaveSingleItem();
        measurement.Tags["from"].ShouldBe("local");
        measurement.Tags["to"].ShouldBe("openrouter");
        measurement.Tags["operation"].ShouldBe("chat");
    }

    [Fact]
    public async Task Streaming_fallback_marks_every_update()
    {
        var handler = new FakeProviderHandler
        {
            Respond = request => request.RequestUri!.Host == "local.test"
                ? FakeProviderHandler.Json("""{"error":{"message":"down"}}""", HttpStatusCode.ServiceUnavailable)
                : FakeProviderHandler.StreamResponse(request),
        };
        using var host = AiTestHost.Build(handler, new() { ["AI:FallbackChatProfile"] = "openrouter" });

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in host.Services.GetRequiredService<IChatClient>().GetStreamingResponseAsync("Hi", cancellationToken: Ct))
        {
            updates.Add(update);
        }

        string.Concat(updates.Select(u => u.Text)).ShouldBe("Hello");
        updates.ShouldAllBe(u => FallbackChatClient.FallbackProfileOf(u.AdditionalProperties) == "openrouter");
    }

    [Fact]
    public void Without_a_fallback_profile_the_unkeyed_client_is_the_active_profile()
    {
        using var host = AiTestHost.Build(new FakeProviderHandler());

        host.Services.GetRequiredService<IChatClient>().GetService<FallbackChatClient>().ShouldBeNull();
    }
}

/// <summary>Measurements of one instrument of the meters a <see cref="TestMeterFactory"/> created (not of parallel tests).</summary>
internal sealed class OwnedMeasurements : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(double Value, Dictionary<string, object?> Tags)> _values = [];

    public OwnedMeasurements(TestMeterFactory factory, string instrument)
    {
        _listener.InstrumentPublished = (i, listener) =>
        {
            if (factory.Owns(i.Meter) && i.Name == instrument)
            {
                listener.EnableMeasurementEvents(i);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) => Add(value, tags));
        _listener.SetMeasurementEventCallback<double>((_, value, tags, _) => Add(value, tags));
        _listener.Start();
    }

    public List<(double Value, Dictionary<string, object?> Tags)> Values
    {
        get
        {
            lock (_values)
            {
                return [.. _values];
            }
        }
    }

    /// <summary>Reads observable instruments (gauges) now.</summary>
    public void Observe() => _listener.RecordObservableInstruments();

    public void Dispose() => _listener.Dispose();

    private void Add(double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        lock (_values)
        {
            _values.Add((value, copy));
        }
    }
}
