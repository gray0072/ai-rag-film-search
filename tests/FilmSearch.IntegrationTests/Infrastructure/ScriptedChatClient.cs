using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>
/// Fake chat model for every chat profile: answers the last user message from a script (<see cref="Script"/>), keyed by
/// that message, so tests that share one API instance do not see each other's answers. An unscripted message gets
/// <see cref="Unscripted"/> (not JSON: query understanding falls back). Records the requests.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    public const string Unscripted = "I am not a JSON answer.";

    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _answers = new(StringComparer.Ordinal);

    public ConcurrentBag<string> Requests { get; } = [];

    /// <summary>Answers for a user message, in order; the last one repeats.</summary>
    public void Script(string userMessage, params string[] answers) => _answers[userMessage] = new ConcurrentQueue<string>(answers);

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var first = messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text).FirstOrDefault() ?? "";
        Requests.Add(first);

        var answer = Unscripted;
        if (_answers.TryGetValue(first, out var queue))
        {
            answer = queue.Count > 1 && queue.TryDequeue(out var next) ? next : queue.TryPeek(out var last) ? last : Unscripted;
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
