using System.Text;
using System.Text.Json;
using FilmSearch.AI.Prompts;
using FilmSearch.AI.Providers;
using FilmSearch.Core.Assistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FilmSearch.AI.Assistant;

/// <summary>A new rolling summary and the tokens it cost.</summary>
internal sealed record ConversationSummary(string Text, long InputTokens, long OutputTokens);

/// <summary>
/// Rolling summary (spec 06 §6): when turns fall out of the history window, the model folds them into the summary stored
/// on the conversation, so a long conversation keeps its facts (what the user wants, which movies were shown, with ids)
/// at a fixed cost per turn. A failed summary is not an error: the turn continues with the window only and the
/// summary is retried on the next turn.
/// </summary>
internal sealed partial class ConversationSummarizer(
    PromptLibrary prompts,
    IOptions<AssistantOptions> options,
    ILogger<ConversationSummarizer> logger)
{
    public const string PromptName = "assistant-summary";

    public async Task<ConversationSummary?> SummarizeAsync(
        IChatClient chat, string? previousSummary, IReadOnlyList<ConversationMessage> older, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var prompt = prompts.Get(PromptName, settings.SummaryPromptVersion);
        List<ChatMessage> messages =
        [
            new(ChatRole.System, prompt.Render(new Dictionary<string, string>())),
            new(ChatRole.User, Transcript(previousSummary, older)),
        ];

        try
        {
            var response = await chat.GetResponseAsync(
                messages,
                new ChatOptions { Temperature = 0, MaxOutputTokens = settings.SummaryMaxOutputTokens },
                cancellationToken);
            var text = response.Text.Trim();
            if (text.Length == 0)
            {
                LogEmpty(logger, prompt.Id);
                return null;
            }

            return new ConversationSummary(text, response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);
        }
        catch (Exception ex) when (AiProviderErrors.IsProviderFailure(ex, cancellationToken))
        {
            LogFailed(logger, prompt.Id, ex);
            return null;
        }
    }

    /// <summary>The text to summarize: the earlier summary, then the visible messages and the movies the tools found.</summary>
    internal static string Transcript(string? previousSummary, IReadOnlyList<ConversationMessage> older)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(previousSummary))
        {
            builder.Append("Earlier summary:\n").Append(previousSummary.Trim()).Append("\n\n");
        }

        builder.Append("Conversation:\n");
        foreach (var message in older)
        {
            switch (message.Role)
            {
                case MessageRole.User:
                    builder.Append("User: ").Append(message.Content.Trim()).Append('\n');
                    break;
                case MessageRole.Assistant when message.IsVisible:
                    builder.Append("Assistant: ").Append(message.Content.Trim()).Append('\n');
                    break;
                case MessageRole.Tool:
                    var movies = FoundMovies(message.Content);
                    if (movies.Length > 0)
                    {
                        builder.Append("(Tool found: ").Append(movies).Append(")\n");
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static string FoundMovies(string toolResult)
    {
        try
        {
            using var document = JsonDocument.Parse(ConversationHistory.CompactToolResult(toolResult));
            if (!document.RootElement.TryGetProperty("movies", out var movies) || movies.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            return string.Join("; ", movies.EnumerateArray().Select(m =>
                $"{m.GetProperty("title").GetString()} ({(m.TryGetProperty("year", out var year) ? year.ToString() : "?")}) [{m.GetProperty("id").GetString()}]"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation summary with prompt {PromptId} failed; the turn continues without the older messages")]
    private static partial void LogFailed(ILogger logger, string promptId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation summary with prompt {PromptId} was empty")]
    private static partial void LogEmpty(ILogger logger, string promptId);
}
