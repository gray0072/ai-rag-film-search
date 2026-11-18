using System.Text.Json;
using FilmSearch.AI.Assistant.Tools;
using FilmSearch.Core.Assistant;
using Microsoft.Extensions.AI;

namespace FilmSearch.AI.Assistant;

/// <summary>
/// Context window management (spec 06 §6). The model gets: system prompt (+ rolling summary) → the last turns that fit
/// into <see cref="AssistantOptions.HistoryMaxMessages"/> and <see cref="AssistantOptions.HistoryTokenBudget"/> → the new
/// user message. Earlier turns keep their tool calls, but their tool results are cut to id, title and year: enough to
/// refer to "the second movie" later, much cheaper than the full results.
/// </summary>
internal static class ConversationHistory
{
    /// <summary>Rough token estimate (≈ 4 characters per token for English; Cyrillic needs more, so the budget has slack).</summary>
    public static int EstimateTokens(ConversationMessage message) =>
        ((message.Content.Length + (message.ToolCallsJson?.Length ?? 0)) / 4) + 4;

    /// <summary>
    /// Splits the unsummarized messages into <c>Older</c> (to fold into the summary) and <c>Window</c> (sent as they are).
    /// The cut is always at a user message, so a turn is never split; the last turn is always kept.
    /// </summary>
    public static (IReadOnlyList<ConversationMessage> Older, IReadOnlyList<ConversationMessage> Window) Split(
        IReadOnlyList<ConversationMessage> messages, int maxVisibleMessages, int tokenBudget)
    {
        var cut = messages.Count;
        var visible = 0;
        var tokens = 0;

        for (var end = messages.Count; end > 0;)
        {
            var start = end - 1;
            while (start > 0 && messages[start].Role != MessageRole.User)
            {
                start--;
            }

            var turn = messages.Skip(start).Take(end - start).ToList();
            visible += turn.Count(m => m.IsVisible);
            tokens += turn.Sum(EstimateTokens);
            if (cut < messages.Count && (visible > maxVisibleMessages || tokens > tokenBudget))
            {
                break;
            }

            cut = start;
            end = start;
        }

        return (messages.Take(cut).ToList(), messages.Skip(cut).ToList());
    }

    /// <summary>
    /// Stored messages of earlier turns as chat messages for the model. A user message without an answer (the user
    /// pressed Stop, or the turn failed) is joined with the next user message: some chat templates (Gemma) require
    /// user and assistant messages to alternate, and dropping it lost the context ("Recommend three thrillers…" →
    /// Stop → "Just one, please." became a request without a topic; I14 case <c>en-real-just-one-after-stop</c>).
    /// Add the new message of the turn with <see cref="AddUserMessage"/>, so it is joined the same way.
    /// </summary>
    public static List<ChatMessage> ToChatMessages(IReadOnlyList<ConversationMessage> messages)
    {
        var result = new List<ChatMessage>(messages.Count);
        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case MessageRole.User:
                    AddUserMessage(result, message.Content);
                    break;
                case MessageRole.Assistant when message.ToolCallsJson is not null:
                    // The text of a tool round is also part of the answer of the turn, so only the calls are sent.
                    result.Add(new ChatMessage(ChatRole.Assistant, [.. ParseToolCalls(message.ToolCallsJson)]));
                    break;
                case MessageRole.Assistant:
                    result.Add(new ChatMessage(ChatRole.Assistant, message.Content));
                    break;
                case MessageRole.Tool:
                    result.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(message.ToolCallId ?? "", CompactToolResult(message.Content))]));
                    break;
            }
        }

        return result;
    }

    /// <summary>Adds a user message, or joins it to the last message when that is a user message without an answer.</summary>
    public static void AddUserMessage(List<ChatMessage> messages, string text)
    {
        if (messages.Count > 0 && messages[^1].Role == ChatRole.User)
        {
            messages[^1] = new ChatMessage(ChatRole.User, $"{messages[^1].Text}\n\n{text}");
            return;
        }

        messages.Add(new ChatMessage(ChatRole.User, text));
    }

    /// <summary>Earlier tool results: movies cut to id, title and year; other results as they are, up to 1000 characters.</summary>
    internal static string CompactToolResult(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("movies", out var movies)
                && movies.ValueKind == JsonValueKind.Array)
            {
                var compact = movies.EnumerateArray().Select(m => new
                {
                    id = m.TryGetProperty("id", out var id) ? id.GetString() : null,
                    title = m.TryGetProperty("title", out var title) ? title.GetString() : null,
                    year = m.TryGetProperty("year", out var year) && year.ValueKind == JsonValueKind.Number ? year.GetInt32() : (int?)null,
                });
                return JsonSerializer.Serialize(new { movies = compact }, ToolJson.Options);
            }
        }
        catch (JsonException)
        {
            // Not JSON: keep the text.
        }

        return content.Length <= 1000 ? content : string.Concat(content.AsSpan(0, 1000), "…");
    }

    public static string SerializeToolCalls(IEnumerable<FunctionCallContent> calls) =>
        JsonSerializer.Serialize(calls.Select(c => new StoredToolCall(c.CallId, c.Name, c.Arguments)), ToolJson.Options);

    public static IEnumerable<FunctionCallContent> ParseToolCalls(string json) =>
        (JsonSerializer.Deserialize<List<StoredToolCall>>(json, ToolJson.Options) ?? [])
            .Select(c => new FunctionCallContent(c.CallId, c.Name, c.Arguments));

    private sealed record StoredToolCall(string CallId, string Name, IDictionary<string, object?>? Arguments);
}
