using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>
/// Fake chat model for every chat profile: answers the last user message from a script (<see cref="Script"/>), keyed by
/// that message, so tests that share one API instance do not see each other's answers. An unscripted message gets
/// <see cref="Unscripted"/> (not JSON: query understanding falls back). Records the requests.
/// Streaming calls (the assistant) are scripted with <see cref="ScriptStream"/>, keyed by the <b>last</b> user message:
/// one script per model call of the turn, each content one update, plus 100 input / 10 output tokens of usage.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    public const string Unscripted = "I am not a JSON answer.";

    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _answers = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, ConcurrentQueue<AIContent[]>> _streams = new(StringComparer.Ordinal);

    public ConcurrentBag<string> Requests { get; } = [];

    /// <summary>Messages and options of every streaming call.</summary>
    public ConcurrentQueue<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> StreamRequests { get; } = [];

    /// <summary>Answers for a user message, in order; the last one repeats.</summary>
    public void Script(string userMessage, params string[] answers) => _answers[userMessage] = new ConcurrentQueue<string>(answers);

    /// <summary>Answers of the streaming calls of a turn whose last user message is <paramref name="userMessage"/>, in order; the last one repeats.</summary>
    public void ScriptStream(string userMessage, params AIContent[][] calls) => _streams[userMessage] = new ConcurrentQueue<AIContent[]>(calls);

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

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        StreamRequests.Enqueue((list, options));
        var last = list.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";

        AIContent[] contents = [new TextContent(Unscripted)];
        if (_streams.TryGetValue(last, out var queue))
        {
            contents = queue.Count > 1 && queue.TryDequeue(out var next) ? next : queue.TryPeek(out var final) ? final : contents;
        }

        foreach (var content in contents)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [content]) { ModelId = "scripted" };
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 })]);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
