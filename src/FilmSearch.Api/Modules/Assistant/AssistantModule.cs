using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FilmSearch.AI.Assistant;
using FilmSearch.AI.Providers;
using FilmSearch.AI.Telemetry;
using FilmSearch.Api.Localization;
using FilmSearch.Api.Modules.Commerce;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using FilmSearch.Infrastructure.Assistant;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace FilmSearch.Api.Modules.Assistant;

/// <summary>Assistant endpoints (spec 04 "Assistant", spec 06): conversations, streamed answers (SSE), checkout confirmations (I12) and feedback. From I11.</summary>
public static class AssistantModule
{
    public const string EventStreamContentType = "text/event-stream";

    public static WebApplicationBuilder AddAssistantModule(this WebApplicationBuilder builder)
    {
        builder.Services.AddAssistantInfrastructure();
        builder.Services.AddAssistant(builder.Configuration);
        builder.Services.AddLangfuseScores(builder.Configuration);
        return builder;
    }

    public static IEndpointRouteBuilder MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        var chat = app.MapGroup("/api/chat").WithTags("Assistant");

        chat.MapPost("/conversations", CreateConversation)
            .WithName("CreateConversation")
            .WithSummary("Starts a conversation with the assistant. The body is optional.");
        chat.MapGet("/conversations", ListConversations)
            .WithName("ListConversations")
            .WithSummary("The owner's conversations, most recently updated first (at most 50).");
        chat.MapGet("/conversations/{id:guid}", GetConversation)
            .WithName("GetConversation")
            .WithSummary("A conversation with its user messages and answers; movie cards with current prices in the Accept-Language locale.");
        chat.MapPost("/conversations/{id:guid}/messages", SendMessage)
            .WithName("SendMessage")
            .WithSummary("Sends a message and streams the answer as server-sent events: message.started, message.delta, tool.started, tool.completed, movies, cart.updated, confirmation.required, message.completed or error (spec 04).")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        chat.MapPost("/conversations/{id:guid}/confirmations/{confirmationId:guid}", AnswerConfirmation)
            .WithName("AnswerConfirmation")
            .WithSummary("The user's answer to a confirmation.required event. Approved: creates the order from the unchanged cart and the payment session and returns checkoutUrl; the model alone can never do this.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        chat.MapPost("/messages/{messageId:guid}/feedback", SaveFeedback)
            .WithName("SaveMessageFeedback")
            .WithSummary("Rates an answer with thumbs up (1) or down (-1); a new rating replaces the old one.");

        return app;
    }

    private static async Task<Created<ConversationListItem>> CreateConversation(
        CreateConversationRequest? request,
        IConversationStore store,
        IOptions<AiOptions> ai,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var conversation = store.Add(request?.Locale ?? httpContext.GetLocale(), ai.Value.ActiveChatProfile);
        await store.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/chat/conversations/{conversation.Id}", ConversationListItem.From(conversation));
    }

    private static async Task<Ok<IReadOnlyList<ConversationListItem>>> ListConversations(IConversationStore store, CancellationToken cancellationToken)
    {
        var conversations = await store.ListAsync(AssistantLimits.ConversationListLimit, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<ConversationListItem>>([.. conversations.Select(ConversationListItem.From)]);
    }

    private static async Task<Results<Ok<ConversationView>, NotFound>> GetConversation(
        Guid id,
        IConversationStore store,
        ICatalogReader catalog,
        ICheckoutConfirmations confirmations,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var conversation = await store.FindAsync(id, cancellationToken);
        if (conversation is null)
        {
            return TypedResults.NotFound();
        }

        var messages = (await store.GetMessagesAsync(id, afterSeq: 0, cancellationToken)).Where(m => m.IsVisible).ToList();
        var movieIds = messages.SelectMany(m => m.MovieIds).Distinct().ToList();
        var movies = (await catalog.GetMoviesAsync(movieIds, MessageLocale(httpContext, conversation), cancellationToken)).ToDictionary(m => m.Id);
        var feedback = await store.GetFeedbackAsync([.. messages.Where(m => m.Role == MessageRole.Assistant).Select(m => m.Id)], cancellationToken);

        // The latest confirmation of each answer: the UI shows its button (or its final state) after a reload.
        var buttons = (await confirmations.ListAsync(id, cancellationToken))
            .GroupBy(c => c.MessageId)
            .ToDictionary(g => g.Key, g => g.Last());

        var views = messages.Select(m => new MessageView(
            m.Id,
            m.Role,
            m.Content,
            m.CreatedAt,
            [.. m.MovieIds.Where(movies.ContainsKey).Select(movieId => MovieCard.From(movies[movieId]))],
            m.Model,
            m.PromptVersion,
            m.Role == MessageRole.Assistant ? new UsageView(m.InputTokens ?? 0, m.OutputTokens ?? 0, m.EstimatedCostUsd ?? 0) : null,
            m.LatencyMs,
            feedback.TryGetValue(m.Id, out var f) ? new FeedbackView(f.Rating, f.Comment) : null,
            buttons.GetValueOrDefault(m.Id))).ToList();

        return TypedResults.Ok(new ConversationView(
            conversation.Id,
            conversation.Title,
            conversation.Locale,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            new UsageView(conversation.TotalInputTokens, conversation.TotalOutputTokens, conversation.EstimatedCostUsd),
            views));
    }

    private static async Task<Results<ServerSentEventsResult<AssistantEvent>, NotFound, ValidationProblem>> SendMessage(
        Guid id,
        SendMessageRequest request,
        IConversationStore store,
        IAssistantChat assistant,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var text = request.Text!.Trim();
        if (text.Length == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["The message is empty."] });
        }

        var conversation = await store.FindAsync(id, cancellationToken);
        if (conversation is null)
        {
            return TypedResults.NotFound();
        }

        // RequestAborted: a client disconnect cancels the model call (spec 06 §7).
        var events = assistant.SendAsync(conversation, text, MessageLocale(httpContext, conversation), httpContext.RequestAborted);
        return TypedResults.ServerSentEvents(ToSseItems(events, httpContext.RequestAborted));
    }

    private static async Task<Results<Ok<ConfirmationResult>, NotFound, ProblemHttpResult>> AnswerConfirmation(
        Guid id,
        Guid confirmationId,
        ConfirmationAnswerRequest request,
        IConversationStore store,
        ICheckoutConfirmations confirmations,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var conversation = await store.FindAsync(id, cancellationToken);
        if (conversation is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            return TypedResults.Ok(await confirmations.ResolveAsync(
                id, confirmationId, request.Approved!.Value, MessageLocale(httpContext, conversation), cancellationToken));
        }
        catch (ConfirmationException ex)
        {
            return TypedResults.Problem(
                statusCode: ex.Error == ConfirmationError.ConfirmationNotFound ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict,
                title: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = JsonNamingPolicy.CamelCase.ConvertName(ex.Error.ToString()) });
        }
        catch (CommerceException ex)
        {
            return CommerceProblems.From(ex);
        }
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> SaveFeedback(
        Guid messageId,
        MessageFeedbackRequest request,
        IConversationStore store,
        AssistantMetrics metrics,
        IFeedbackScoreSink scores,
        CancellationToken cancellationToken)
    {
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        var rating = request.Rating!.Value;
        var result = await store.SaveFeedbackAsync(messageId, rating, comment, cancellationToken);
        if (result == FeedbackResult.Saved)
        {
            // Online quality signal: a metric, and a score on the turn's trace in Langfuse when it is on (I18).
            metrics.RecordFeedback(rating);
            if (await store.GetTraceIdAsync(messageId, cancellationToken) is { } traceId)
            {
                scores.Enqueue(new FeedbackScore(traceId, messageId, rating, comment));
            }
        }

        return result switch
        {
            FeedbackResult.Saved => TypedResults.NoContent(),
            FeedbackResult.NotAnAnswer => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["messageId"] = ["Only answers of the assistant can be rated."],
            }),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>
    /// Movie texts follow the UI language (Accept-Language) when the client sends it; otherwise the locale of the
    /// conversation (e.g. a curl call). The answer language follows the user's message, not this locale (FR-AST-7).
    /// </summary>
    private static string MessageLocale(HttpContext httpContext, Conversation conversation) =>
        httpContext.Request.Headers.AcceptLanguage.Count > 0 ? httpContext.GetLocale() : conversation.Locale;

    private static async IAsyncEnumerable<SseItem<AssistantEvent>> ToSseItems(
        IAsyncEnumerable<AssistantEvent> events, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in events.WithCancellation(cancellationToken))
        {
            yield return new SseItem<AssistantEvent>(item, item.EventType);
        }
    }
}
