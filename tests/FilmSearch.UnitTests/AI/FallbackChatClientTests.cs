using System.Runtime.CompilerServices;
using FilmSearch.AI.Providers;
using FilmSearch.AI.Telemetry;
using FilmSearch.UnitTests.Assistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace FilmSearch.UnitTests.AI;

/// <summary>When <see cref="FallbackChatClient"/> switches to the fallback profile, and when it must not.</summary>
public sealed class FallbackChatClientTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly TestMeterFactory _meters = new();
    private readonly ChaosChatClient _primary = new("primary");
    private readonly ChaosChatClient _fallback = new("fallback");

    public void Dispose() => _meters.Dispose();

    private FallbackChatClient Create() =>
        new(_primary, "local", _fallback, "backup", new AiMetrics(_meters), NullLogger<FallbackChatClient>.Instance);

    [Fact]
    public async Task Healthy_primary_answers_and_the_fallback_is_not_called()
    {
        var response = await Create().GetResponseAsync("Hi", cancellationToken: Ct);

        response.Text.ShouldBe("primary");
        FallbackChatClient.FallbackProfileOf(response.AdditionalProperties).ShouldBeNull();
        _fallback.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Timeout_before_the_first_update_switches_the_stream_to_the_fallback()
    {
        // What the SDK throws when its network timeout fires: a cancellation the caller did not ask for.
        _primary.Failure = new TaskCanceledException("The operation was cancelled.", new TimeoutException());

        var text = "";
        await foreach (var update in Create().GetStreamingResponseAsync("Hi", cancellationToken: Ct))
        {
            text += update.Text;
            FallbackChatClient.FallbackProfileOf(update.AdditionalProperties).ShouldBe("backup");
        }

        text.ShouldBe("fallback");
    }

    [Fact]
    public async Task Failure_after_the_first_update_is_not_switched()
    {
        // The user has seen part of the answer: starting again with another model would mix two answers.
        _primary.FailAfterFirstUpdate = new HttpRequestException("Connection reset");

        var text = "";
        await Should.ThrowAsync<HttpRequestException>(async () =>
        {
            await foreach (var update in Create().GetStreamingResponseAsync("Hi", cancellationToken: Ct))
            {
                text += update.Text;
            }
        });

        text.ShouldBe("primary");
        _fallback.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Bugs_and_caller_cancellation_are_not_switched()
    {
        _primary.Failure = new InvalidOperationException("A bug in our code");
        await Should.ThrowAsync<InvalidOperationException>(() => Create().GetResponseAsync("Hi", cancellationToken: Ct));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _primary.Failure = new OperationCanceledException(cancelled.Token);
        await Should.ThrowAsync<OperationCanceledException>(() => Create().GetResponseAsync("Hi", cancellationToken: cancelled.Token));

        _fallback.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Both_profiles_down_surfaces_the_fallback_error()
    {
        _primary.Failure = new HttpRequestException("Connection refused");
        _fallback.Failure = new HttpRequestException("Fallback is down too");

        var error = await Should.ThrowAsync<HttpRequestException>(() => Create().GetResponseAsync("Hi", cancellationToken: Ct));

        error.Message.ShouldBe("Fallback is down too");
    }
}

/// <summary>A chat client that answers its name, or fails before or after the first streamed update.</summary>
internal sealed class ChaosChatClient(string answer) : IChatClient
{
    public Exception? Failure { get; set; }

    public Exception? FailAfterFirstUpdate { get; set; }

    public int Calls { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Failure is { } failure
            ? Task.FromException<ChatResponse>(failure)
            : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls++;
        await Task.Yield();
        if (Failure is { } failure)
        {
            throw failure;
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, answer);
        if (FailAfterFirstUpdate is { } late)
        {
            throw late;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
