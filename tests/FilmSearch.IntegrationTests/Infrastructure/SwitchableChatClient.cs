using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Polly.CircuitBreaker;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>How a <see cref="SwitchableChatClient"/> behaves.</summary>
public enum ChatProviderState
{
    /// <summary>Passes calls to the inner client.</summary>
    Up,

    /// <summary>The server is not running: the connection is refused.</summary>
    Refused,

    /// <summary>The model did not answer in time: the SDK's timeout, a cancellation the caller did not ask for.</summary>
    TimedOut,

    /// <summary>The circuit breaker of the profile is open: the call fails without a request.</summary>
    CircuitOpen,
}

/// <summary>A chat profile for chaos tests (I19): up (the scripted model), or failing the way a real provider fails.</summary>
public sealed class SwitchableChatClient(IChatClient inner) : IChatClient
{
    public ChatProviderState State { get; set; }

    public int Failures { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        State == ChatProviderState.Up ? inner.GetResponseAsync(messages, options, cancellationToken) : Task.FromException<ChatResponse>(Failure());

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (State != ChatProviderState.Up)
        {
            throw Failure();
        }

        await foreach (var update in inner.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => inner.GetService(serviceType, serviceKey);

    public void Dispose()
    {
    }

    private Exception Failure()
    {
        Failures++;
        return State switch
        {
            ChatProviderState.Refused => new HttpRequestException("Connection refused (localhost:1234)"),
            ChatProviderState.TimedOut => new TaskCanceledException("The operation was cancelled.", new TimeoutException("The operation has timed out.")),
            _ => new BrokenCircuitException("The circuit is now open and is not allowing calls."),
        };
    }
}
