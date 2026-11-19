using System.Text;
using FilmSearch.Core.Commerce;
using FilmSearch.Infrastructure.Commerce;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FilmSearch.Api.Modules.Commerce;

/// <summary>
/// Webhook endpoints of the payment providers (spec 07 §5). Each provider has its own path because the signature
/// scheme differs; after the gateway has verified and parsed the event, processing is the same for all of them.
/// Not in the OpenAPI document: only providers call them.
/// </summary>
public static partial class PaymentWebhookEndpoints
{
    public const string StripePath = "/api/payments/webhooks/stripe";

    public static IEndpointRouteBuilder MapStripeWebhookEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost(StripePath, (HttpRequest request, PaymentGateways gateways, IPaymentWebhookProcessor processor, ILoggerFactory loggers, CancellationToken cancellationToken) =>
            gateways.Get(PaymentProvider.Stripe) is { } gateway
                ? ReceiveAsync(request, gateway, processor, loggers, cancellationToken)
                : Task.FromResult<Results<Ok<WebhookAck>, ProblemHttpResult>>(
                    TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Stripe is not configured.")))
            .ExcludeFromDescription();

        return app;
    }

    /// <summary>
    /// Spec 07 §5: invalid signature or body → 400 and nothing stored; a verified event the shop does not act on → 200
    /// "ignored", not stored; a known event → 200; a processing error → 500 (the exception handler), so the provider retries.
    /// </summary>
    public static async Task<Results<Ok<WebhookAck>, ProblemHttpResult>> ReceiveAsync(
        HttpRequest request,
        IPaymentGateway gateway,
        IPaymentWebhookProcessor processor,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        // Signatures cover the exact bytes of the body, so it is read as it is, not through model binding.
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var logger = loggers.CreateLogger(typeof(PaymentWebhookEndpoints));
        var metrics = request.HttpContext.RequestServices.GetRequiredService<CommerceMetrics>();

        ParsedWebhook parsed;
        try
        {
            parsed = gateway.ParseWebhook(new PaymentWebhookRequest(body, headers));
        }
        catch (Exception ex) when (ex is WebhookSignatureException or FormatException)
        {
            LogRejected(logger, gateway.Provider, ex.Message);
            metrics.RecordWebhook(gateway.Provider, "unknown", CommerceMetrics.Rejected);
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid webhook.", detail: ex.Message);
        }

        if (parsed.Event is null)
        {
            LogIgnored(logger, gateway.Provider, parsed.ProviderEventId, parsed.ProviderEventType, parsed.IgnoredReason);
            metrics.RecordWebhook(gateway.Provider, parsed.ProviderEventType, CommerceMetrics.Unhandled);
            return TypedResults.Ok(new WebhookAck(parsed.ProviderEventId, WebhookProcessingResult.Ignored));
        }

        var type = CommerceMetrics.Name(parsed.Event.Type.ToString());
        WebhookProcessingResult result;
        try
        {
            result = await processor.ProcessAsync(gateway.Provider, parsed.Event, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metrics.RecordWebhook(gateway.Provider, type, CommerceMetrics.Error);
            throw;
        }

        metrics.RecordWebhook(gateway.Provider, type, result);
        return TypedResults.Ok(new WebhookAck(parsed.ProviderEventId, result));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a {Provider} webhook: {Reason}")]
    private static partial void LogRejected(ILogger logger, PaymentProvider provider, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored {Provider} webhook {EventId} ({EventType}): {Reason}")]
    private static partial void LogIgnored(ILogger logger, PaymentProvider provider, string eventId, string eventType, string? reason);
}

/// <param name="EventId">The provider event id.</param>
/// <param name="Result">What processing did with the event.</param>
public sealed record WebhookAck(string EventId, WebhookProcessingResult Result);
