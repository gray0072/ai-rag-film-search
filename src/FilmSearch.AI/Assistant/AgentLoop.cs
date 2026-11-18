using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FilmSearch.AI.Assistant.Tools;
using FilmSearch.AI.Telemetry;
using FilmSearch.Core.Assistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FilmSearch.AI.Assistant;

/// <summary>What one turn of the loop produced; filled while the events stream.</summary>
internal sealed class TurnState
{
    private readonly HashSet<Guid> _movieIds = [];

    /// <summary>All answer text the user saw, over all iterations.</summary>
    public StringBuilder Text { get; } = new();

    /// <summary>Assistant messages with tool calls and the tool results, in order (stored with the answer).</summary>
    public List<ChatMessage> ToolRounds { get; } = [];

    /// <summary>Movies shown as cards, in order of appearance.</summary>
    public List<Guid> MovieIds { get; } = [];

    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }

    /// <summary>Model id reported by the provider.</summary>
    public string? Model { get; private set; }

    public int ModelCalls { get; private set; }

    public bool IterationLimitHit { get; set; }

    public void AddUsage(ChatResponse response)
    {
        ModelCalls++;
        InputTokens += response.Usage?.InputTokenCount ?? 0;
        OutputTokens += response.Usage?.OutputTokenCount ?? 0;
        Model ??= response.ModelId;
    }

    /// <summary>True when the movie was not shown in this turn yet.</summary>
    public bool AddMovie(Guid movieId)
    {
        if (!_movieIds.Add(movieId))
        {
            return false;
        }

        MovieIds.Add(movieId);
        return true;
    }
}

/// <summary>
/// The agent loop (spec 06 §2, ADR-0006): stream a model answer with the tools; if it asks for tools, run them, append the
/// results and ask again; stop when the model answers without tool calls. After <c>maxToolIterations</c> calls with tools,
/// one last call without tools makes the model answer with what it found. Text is streamed as it comes; tool calls and
/// their results become events (<c>tool.*</c>, <c>movies</c>, and the tool's own: <c>cart.updated</c>,
/// <c>confirmation.required</c>) and messages of the turn.
/// </summary>
internal sealed partial class AgentLoop(
    IChatClient chat,
    IReadOnlyList<AssistantTool> tools,
    IList<AITool> declarations,
    int maxToolIterations,
    ILogger logger)
{
    /// <summary>Sent as a user message when the tool budget of the turn is used up (not stored).</summary>
    internal const string IterationLimitNote =
        "(The tool limit for this answer is reached. Answer the user now with the movies you found, without calling tools.)";

    private const int ArgsSummaryLength = 200;

    private static readonly ActivitySource Activities = new(AiMetrics.SourceName);

    public async Task RunAsync(
        List<ChatMessage> messages,
        ToolContext context,
        TurnState state,
        Action<AssistantEvent> emit,
        CancellationToken cancellationToken)
    {
        for (var iteration = 1; ; iteration++)
        {
            var withTools = iteration <= maxToolIterations;
            if (!withTools)
            {
                state.IterationLimitHit = true;
                LogIterationLimit(logger, context.ConversationId, maxToolIterations);
                messages.Add(new ChatMessage(ChatRole.User, IterationLimitNote));
            }

            var chatOptions = withTools ? new ChatOptions { Tools = declarations, ToolMode = ChatToolMode.Auto } : new ChatOptions();
            var updates = new List<ChatResponseUpdate>();
            var firstDelta = true;

            await foreach (var update in chat.GetStreamingResponseAsync(messages, chatOptions, cancellationToken))
            {
                updates.Add(update);
                var delta = update.Text;
                if (delta.Length == 0)
                {
                    continue;
                }

                // Text of an earlier iteration ("Let me search…") and of this one are separate paragraphs.
                if (firstDelta && state.Text.Length > 0)
                {
                    delta = "\n\n" + delta;
                }

                firstDelta = false;
                state.Text.Append(delta);
                emit(new MessageDeltaEvent(delta));
            }

            var response = updates.ToChatResponse();
            state.AddUsage(response);

            var contents = response.Messages.SelectMany(m => m.Contents).ToList();
            var calls = contents.OfType<FunctionCallContent>().ToList();
            if (calls.Count == 0 || !withTools)
            {
                return;
            }

            var round = new ChatMessage(ChatRole.Assistant, [.. contents.Where(c => c is TextContent or FunctionCallContent)]);
            messages.Add(round);
            state.ToolRounds.Add(round);

            // Several calls in one response (parallel tool calls) run one after another, in the model's order: tools
            // share the request's DbContext, and "add_to_cart, then request_checkout" must see the added line.
            foreach (var call in calls)
            {
                emit(new ToolStartedEvent(call.CallId, call.Name, SummarizeArguments(call.Arguments)));
                var outcome = await RunToolAsync(call, context, cancellationToken);
                emit(new ToolCompletedEvent(call.CallId, call.Name, outcome.Ok));

                var cards = outcome.Movies.Where(m => state.AddMovie(m.Id)).ToList();
                if (cards.Count > 0)
                {
                    emit(new MoviesEvent(cards));
                }

                foreach (var item in outcome.Events)
                {
                    emit(item);
                }

                var result = new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, outcome.Result)]);
                messages.Add(result);
                state.ToolRounds.Add(result);
            }
        }
    }

    /// <summary>Runs one call. Unknown tools, unparsable arguments and tool failures become an error result the model can read.</summary>
    private async Task<ToolOutcome> RunToolAsync(FunctionCallContent call, ToolContext context, CancellationToken cancellationToken)
    {
        // GenAI semantic conventions: one execute_tool span per call, a child of the turn span, next to the chat spans.
        using var activity = Activities.StartActivity($"execute_tool {call.Name}");
        activity?.SetTag("gen_ai.operation.name", "execute_tool");
        activity?.SetTag("gen_ai.tool.name", call.Name);
        activity?.SetTag("gen_ai.tool.call.id", call.CallId);

        ToolOutcome outcome;
        var tool = tools.FirstOrDefault(t => t.Name == call.Name);
        if (tool is null)
        {
            outcome = ToolOutcome.Error($"Unknown tool '{call.Name}'. Available tools: {string.Join(", ", tools.Select(t => t.Name))}.");
        }
        else if (call.Exception is not null)
        {
            outcome = ToolOutcome.Error($"The arguments of {call.Name} are not valid JSON: {call.Exception.Message}");
        }
        else
        {
            try
            {
                outcome = await tool.InvokeAsync(call.Arguments, context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogToolFailed(logger, call.Name, ex);
                outcome = ToolOutcome.Error($"{call.Name} failed. Try again with other arguments, or tell the user that the search does not work right now.");
            }
        }

        if (!outcome.Ok)
        {
            LogToolRefused(logger, call.Name, outcome.Result);
            activity?.SetStatus(ActivityStatusCode.Error, outcome.Result);
        }

        return outcome;
    }

    internal static string SummarizeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return "";
        }

        var json = JsonSerializer.Serialize(arguments.Where(a => a.Value is not null).ToDictionary(), ToolJson.Options);
        return json.Length <= ArgsSummaryLength ? json : string.Concat(json.AsSpan(0, ArgsSummaryLength - 1), "…");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation {ConversationId}: tool iteration limit {Limit} reached; answering without tools")]
    private static partial void LogIterationLimit(ILogger logger, Guid conversationId, int limit);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool {Tool} failed")]
    private static partial void LogToolFailed(ILogger logger, string tool, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool {Tool} returned an error to the model: {Result}")]
    private static partial void LogToolRefused(ILogger logger, string tool, string result);
}
