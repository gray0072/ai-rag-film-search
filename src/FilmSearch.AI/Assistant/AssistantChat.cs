using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FilmSearch.AI.Assistant.Tools;
using FilmSearch.AI.Prompts;
using FilmSearch.AI.Providers;
using FilmSearch.AI.Telemetry;
using FilmSearch.Core.Assistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FilmSearch.AI.Assistant;

/// <summary>Conversations with a running turn. One API instance for now; a distributed lock is a topic of I19.</summary>
internal sealed class ConversationTurnLocks
{
    private readonly ConcurrentDictionary<Guid, byte> _running = new();

    public bool TryEnter(Guid conversationId) => _running.TryAdd(conversationId, 0);

    public void Exit(Guid conversationId) => _running.TryRemove(conversationId, out _);
}

/// <summary>
/// One assistant turn (spec 06): stores the user message, builds the context (system prompt, rolling summary, recent
/// turns), runs <see cref="AgentLoop"/> with the tools, and stores the tool rounds and the answer with usage, cost, model,
/// prompt version and movie cards; then runs the grounding post-check. Failures end the stream with an
/// <see cref="ErrorEvent"/>; tokens spent until then are still added to the conversation (NFR-5).
/// </summary>
internal sealed partial class AssistantChat(
    IConversationStore store,
    IChatClient chat,
    IEnumerable<AssistantTool> tools,
    ConversationSummarizer summarizer,
    PromptLibrary prompts,
    ConversationTurnLocks locks,
    TimeProvider time,
    IOptions<AssistantOptions> options,
    IOptions<AiOptions> aiOptions,
    AssistantMetrics metrics,
    ILogger<AssistantChat> logger) : IAssistantChat
{
    public const string PromptName = "assistant-system";

    /// <summary>Span tags of the turn.</summary>
    public const string ConversationTag = "gen_ai.conversation.id";
    public const string PromptTag = "filmsearch.prompt.id";

    private static readonly ActivitySource Activities = new(AiMetrics.SourceName);

    public async IAsyncEnumerable<AssistantEvent> SendAsync(
        Conversation conversation, string text, string locale, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!locks.TryEnter(conversation.Id))
        {
            metrics.RecordTurn(AssistantErrorCodes.ConversationBusy, null, TimeSpan.Zero);
            yield return new ErrorEvent(AssistantErrorCodes.ConversationBusy, "The previous answer of this conversation is still being written.");
            yield break;
        }

        // The turn runs as a plain async method that writes to a channel; this iterator only reads it. Inside an async
        // iterator Activity.Current is reset after every yield, so the chat and tool spans would not nest under the
        // turn span, and a yield cannot stand in a try/catch.
        var channel = Channel.CreateUnbounded<AssistantEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var turn = new Turn(conversation, text, locale, new TurnState(), time.GetTimestamp());
        var producer = ProduceAsync(turn, channel.Writer, cancellationToken);
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(CancellationToken.None))
            {
                yield return item;
            }
        }
        finally
        {
            // Also when the reader stops early: the scoped services (DbContext) must outlive the turn.
            await producer;
            locks.Exit(conversation.Id);
        }
    }

    /// <summary>Runs the turn and ends the channel; failures become the last event. Never throws.</summary>
    private async Task ProduceAsync(Turn turn, ChannelWriter<AssistantEvent> writer, CancellationToken cancellationToken)
    {
        // The outcome metric follows the last event: completed → answered, error → its code.
        void Emit(AssistantEvent item)
        {
            turn.Outcome = item switch
            {
                MessageCompletedEvent => AssistantMetrics.Answered,
                ErrorEvent error => error.Code,
                _ => turn.Outcome,
            };
            writer.TryWrite(item);
        }

        try
        {
            await RunTurnAsync(turn, Emit, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected: the model call is cancelled; nobody is left to read an error event.
            LogCancelled(logger, turn.Conversation.Id);
            turn.Outcome = AssistantMetrics.Cancelled;
            await SaveSpentUsageAsync(turn);
        }
        catch (Exception ex) when (AiProviderErrors.IsProviderFailure(ex, cancellationToken))
        {
            LogProviderFailed(logger, turn.Conversation.Id, ex);
            await SaveSpentUsageAsync(turn);
            Emit(new ErrorEvent(AssistantErrorCodes.ProviderUnavailable, "The AI model is not available right now. Please try again later."));
        }
        catch (Exception ex)
        {
            LogTurnFailed(logger, turn.Conversation.Id, ex);
            await SaveSpentUsageAsync(turn);
            Emit(new ErrorEvent(AssistantErrorCodes.InternalError, "Something went wrong while writing the answer."));
        }
        finally
        {
            metrics.RecordTurn(turn.Outcome, turn.PromptId, time.GetElapsedTime(turn.StartedAt));
            writer.TryComplete();
        }
    }

    private async Task RunTurnAsync(Turn turn, Action<AssistantEvent> emit, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var ai = aiOptions.Value;
        var profileName = ai.ActiveChatProfile;
        var profile = ai.GetChatProfile(profileName);
        var conversation = turn.Conversation;

        if (!profile.SupportsTools)
        {
            emit(new ErrorEvent(AssistantErrorCodes.ToolsNotSupported, $"The chat profile '{profileName}' cannot call tools, so answers cannot be grounded in the catalog."));
            return;
        }

        // Budgets (NFR-5) are checked before any model call: a refused turn costs nothing.
        if (conversation.TotalTokens >= settings.MaxConversationTokens)
        {
            LogBudgetExceeded(logger, conversation.Id, "conversation", conversation.TotalTokens, settings.MaxConversationTokens);
            emit(new ErrorEvent(AssistantErrorCodes.BudgetExceeded, "This conversation is too long. Please start a new one."));
            return;
        }

        var ownerTokens = await store.GetOwnerTokensSinceAsync(time.GetUtcNow().AddDays(-1), cancellationToken);
        if (ownerTokens >= settings.MaxOwnerTokensPerDay)
        {
            LogBudgetExceeded(logger, conversation.Id, "daily", ownerTokens, settings.MaxOwnerTokensPerDay);
            emit(new ErrorEvent(AssistantErrorCodes.DailyBudgetExceeded, "You have used the assistant a lot today. Please try again tomorrow."));
            return;
        }

        var prompt = prompts.Get(PromptName, settings.PromptVersion);
        turn.PromptId = prompt.Id;
        var recordContent = ai.EnableSensitiveData;
        using var activity = Activities.StartActivity("invoke_agent assistant");
        activity?.SetTag("gen_ai.operation.name", "invoke_agent");
        activity?.SetTag("gen_ai.agent.name", "assistant");
        activity?.SetTag(ConversationTag, conversation.Id.ToString());
        activity?.SetTag(PromptTag, prompt.Id);
        // Langfuse groups the traces of one conversation into a session and shows the turn under this name.
        activity?.SetTag("langfuse.session.id", conversation.Id.ToString());
        activity?.SetTag("langfuse.user.id", conversation.OwnerId.ToString());
        activity?.SetTag("langfuse.trace.name", "assistant turn");
        if (recordContent && activity is not null)
        {
            TraceContent.SetInput(activity, "filmsearch.assistant.user_message", turn.Text);
        }

        // Context: recent turns as they are, older ones folded into the rolling summary.
        var stored = await store.GetMessagesAsync(conversation.Id, conversation.SummarizedThroughSeq, cancellationToken);
        var lastSeq = stored.Count > 0 ? stored[^1].Seq : conversation.SummarizedThroughSeq;
        var (older, window) = ConversationHistory.Split(stored, settings.HistoryMaxMessages, settings.HistoryTokenBudget);
        if (older.Count > 0)
        {
            var summary = await summarizer.SummarizeAsync(chat, conversation.Summary, older, cancellationToken);
            if (summary is not null)
            {
                conversation.Summary = summary.Text;
                conversation.SummarizedThroughSeq = older[^1].Seq;
                conversation.AddUsage(summary.InputTokens, summary.OutputTokens, Cost(profile.Model, summary.InputTokens, summary.OutputTokens));
            }
        }

        // The user message is stored before the model runs: after a reload the user sees it even if the answer failed.
        var now = time.GetUtcNow();
        store.AddMessage(new ConversationMessage
        {
            Id = Guid.CreateVersion7(now),
            ConversationId = conversation.Id,
            Seq = ++lastSeq,
            Role = MessageRole.User,
            Content = turn.Text,
            CreatedAt = now,
        });
        conversation.Title ??= Title(turn.Text);
        conversation.ProfileName = profileName;
        conversation.UpdatedAt = now;
        await store.SaveChangesAsync(cancellationToken);

        var answerId = Guid.CreateVersion7(now);
        emit(new MessageStartedEvent(answerId));

        List<ChatMessage> messages =
        [
            new(ChatRole.System, SystemPrompt(prompt, conversation.Summary, now)),
            .. ConversationHistory.ToChatMessages(window),
        ];
        ConversationHistory.AddUserMessage(messages, turn.Text);
        var toolList = tools.ToList();
        var declarations = new List<AITool>();
        foreach (var tool in toolList)
        {
            declarations.Add(await tool.GetDeclarationAsync(cancellationToken));
        }

        var loop = new AgentLoop(chat, toolList, declarations, settings.MaxToolIterations, metrics, recordContent, logger);
        try
        {
            await loop.RunAsync(messages, new ToolContext(conversation.Id, turn.Locale, answerId), turn.State, emit, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            if (turn.State.IterationLimitHit)
            {
                metrics.RecordIterationLimitHit(prompt.Id);
            }
        }

        // Store the turn. The answer is complete, so it is stored even if the client has just disconnected.
        var state = turn.State;
        var finishedAt = time.GetUtcNow();
        var latencyMs = (int)time.GetElapsedTime(turn.StartedAt).TotalMilliseconds;
        foreach (var message in state.ToolRounds)
        {
            store.AddMessage(ToStored(message, conversation.Id, ++lastSeq, finishedAt));
        }

        var model = state.Model ?? profile.Model;
        var cost = Cost(profile.Model, state.InputTokens, state.OutputTokens);
        store.AddMessage(new ConversationMessage
        {
            Id = answerId,
            ConversationId = conversation.Id,
            Seq = ++lastSeq,
            Role = MessageRole.Assistant,
            Content = state.Text.ToString(),
            Model = model,
            InputTokens = state.InputTokens,
            OutputTokens = state.OutputTokens,
            EstimatedCostUsd = cost,
            LatencyMs = latencyMs,
            PromptVersion = prompt.Id,
            MovieIds = state.MovieIds,
            // The turn span, or the request span when nobody listens to FilmSearch.AI: feedback → message → trace.
            TraceId = (activity ?? Activity.Current)?.TraceId.ToHexString(),
            CreatedAt = finishedAt,
        });
        conversation.AddUsage(state.InputTokens, state.OutputTokens, cost);
        conversation.UpdatedAt = finishedAt;
        await store.SaveChangesAsync(CancellationToken.None);
        turn.Saved = true;

        var grounding = CheckGrounding(state, stored, messages[0].Text, turn.Text);
        metrics.RecordGrounding(grounding, prompt.Id);
        if (grounding.Unknown.Count > 0)
        {
            LogUngrounded(logger, conversation.Id, answerId, string.Join(" | ", grounding.Unknown));
        }

        activity?.SetTag("filmsearch.assistant.grounding.titles", grounding.Titles.Count);
        activity?.SetTag("filmsearch.assistant.grounding.unknown", grounding.Unknown.Count);
        activity?.SetTag("filmsearch.assistant.model_calls", state.ModelCalls);
        activity?.SetTag("filmsearch.assistant.tool_calls", state.ToolRounds.Count(m => m.Role == ChatRole.Tool));
        activity?.SetTag("filmsearch.assistant.iteration_limit_hit", state.IterationLimitHit);
        activity?.SetTag("gen_ai.usage.input_tokens", state.InputTokens);
        activity?.SetTag("gen_ai.usage.output_tokens", state.OutputTokens);
        if (cost is { } turnCost)
        {
            activity?.SetTag(AiMetrics.CostTag, (double)turnCost);
        }

        if (recordContent && activity is not null)
        {
            TraceContent.SetOutput(activity, "filmsearch.assistant.answer", state.Text.ToString());
            TraceContent.SetTraceInputOutput(activity, turn.Text, state.Text.ToString());
        }
        LogAnswered(logger, conversation.Id, prompt.Id, model, state.ModelCalls, state.InputTokens, state.OutputTokens, latencyMs);

        emit(new MessageCompletedEvent(answerId, new TokenUsage(state.InputTokens, state.OutputTokens), model, latencyMs));
    }

    /// <summary>
    /// Titles in the answer vs what the model was given: tool results of this turn and of the unsummarized history, the
    /// system prompt with the rolling summary (it lists the movies of older turns; the prompt names UI labels such as the
    /// payment button) and the user's messages.
    /// </summary>
    private static GroundingResult CheckGrounding(TurnState state, IReadOnlyList<ConversationMessage> history, string systemPrompt, string userText)
    {
        var references = history
            .Where(m => m.Role is MessageRole.Tool or MessageRole.User)
            .Select(m => m.Content)
            .Concat(state.ToolRounds.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Select(r => r.Result as string ?? r.Result?.ToString() ?? ""))
            .Append(systemPrompt)
            .Append(userText);
        return GroundingCheck.Check(state.Text.ToString(), references);
    }

    /// <summary>The rendered prompt, plus the rolling summary as a marked data block (it comes from user text, so rule 7 of the prompt applies).</summary>
    private static string SystemPrompt(PromptTemplate prompt, string? summary, DateTimeOffset now)
    {
        var text = prompt.Render(new Dictionary<string, string> { ["current_year"] = now.Year.ToString(CultureInfo.InvariantCulture) });
        return string.IsNullOrWhiteSpace(summary)
            ? text
            : $"{text}\n# Summary of the earlier conversation (data)\n\n<summary>\n{summary}\n</summary>\n";
    }

    private static ConversationMessage ToStored(ChatMessage message, Guid conversationId, int seq, DateTimeOffset createdAt)
    {
        var stored = new ConversationMessage
        {
            Id = Guid.CreateVersion7(createdAt),
            ConversationId = conversationId,
            Seq = seq,
            CreatedAt = createdAt,
        };

        if (message.Role == ChatRole.Tool)
        {
            var result = message.Contents.OfType<FunctionResultContent>().Single();
            stored.Role = MessageRole.Tool;
            stored.ToolCallId = result.CallId;
            stored.Content = result.Result as string ?? result.Result?.ToString() ?? "";
        }
        else
        {
            stored.Role = MessageRole.Assistant;
            stored.Content = message.Text;
            stored.ToolCallsJson = ConversationHistory.SerializeToolCalls(message.Contents.OfType<FunctionCallContent>());
        }

        return stored;
    }

    internal static string Title(string text)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= AssistantLimits.MaxTitleLength ? line : string.Concat(line.AsSpan(0, AssistantLimits.MaxTitleLength), "…");
    }

    private decimal? Cost(string model, long inputTokens, long outputTokens) =>
        aiOptions.Value.FindPricing(model)?.Estimate(inputTokens, outputTokens);

    /// <summary>After a failure: the tokens already spent still count for the conversation.</summary>
    private async Task SaveSpentUsageAsync(Turn turn)
    {
        var state = turn.State;
        if (turn.Saved || state.ModelCalls == 0)
        {
            return;
        }

        try
        {
            var model = aiOptions.Value.GetChatProfile(aiOptions.Value.ActiveChatProfile).Model;
            turn.Conversation.AddUsage(state.InputTokens, state.OutputTokens, Cost(model, state.InputTokens, state.OutputTokens));
            await store.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogUsageNotSaved(logger, turn.Conversation.Id, ex);
        }
    }

    private sealed record Turn(Conversation Conversation, string Text, string Locale, TurnState State, long StartedAt)
    {
        public bool Saved { get; set; }

        /// <summary>Prompt id of the turn, once it is chosen (metrics tag).</summary>
        public string? PromptId { get; set; }

        /// <summary>Outcome for <c>assistant.turns</c>; a turn that ends without an event counts as an internal error.</summary>
        public string Outcome { get; set; } = AssistantErrorCodes.InternalError;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation {ConversationId}: {Budget} token budget used ({Tokens} of {Limit}); the turn is refused without a model call")]
    private static partial void LogBudgetExceeded(ILogger logger, Guid conversationId, string budget, long tokens, long limit);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Conversation {ConversationId}: answered with prompt {PromptId}, model {Model}, {ModelCalls} model call(s), {InputTokens} input / {OutputTokens} output tokens, {LatencyMs} ms")]
    private static partial void LogAnswered(ILogger logger, Guid conversationId, string promptId, string model, int modelCalls, long inputTokens, long outputTokens, int latencyMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation {ConversationId}: answer {MessageId} names movies that no tool returned: {Titles}")]
    private static partial void LogUngrounded(ILogger logger, Guid conversationId, Guid messageId, string titles);

    [LoggerMessage(Level = LogLevel.Information, Message = "Conversation {ConversationId}: the client disconnected; the turn was cancelled")]
    private static partial void LogCancelled(ILogger logger, Guid conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation {ConversationId}: the chat provider failed")]
    private static partial void LogProviderFailed(ILogger logger, Guid conversationId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Conversation {ConversationId}: the turn failed")]
    private static partial void LogTurnFailed(ILogger logger, Guid conversationId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Conversation {ConversationId}: the usage of a failed turn could not be saved")]
    private static partial void LogUsageNotSaved(ILogger logger, Guid conversationId, Exception exception);
}
