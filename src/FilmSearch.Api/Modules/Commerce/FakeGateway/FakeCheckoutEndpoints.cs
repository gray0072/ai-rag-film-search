using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using FilmSearch.Core.Commerce;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FilmSearch.Api.Modules.Commerce.FakeGateway;

/// <summary>
/// The fake provider's side of HTTP (spec 04 "Commerce", spec 07 §3): the hosted payment page with Pay / Decline /
/// Cancel and the webhook endpoint. Not in the OpenAPI document: the web app only redirects to the page. The page is a
/// stand-in for a provider page, so it is plain English HTML, not part of the localized web app.
/// </summary>
public static class FakeCheckoutEndpoints
{
    public static IEndpointRouteBuilder MapFakeCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        var page = app.MapGroup(FakePaymentGateway.CheckoutPath).ExcludeFromDescription();
        page.MapGet("/{sessionId}", ShowPage);
        page.MapPost("/{sessionId}/pay", Pay);
        page.MapPost("/{sessionId}/decline", Decline);
        page.MapPost("/{sessionId}/cancel", Cancel);

        app.MapPost(FakeWebhookDispatcher.WebhookPath, ReceiveWebhook).ExcludeFromDescription();

        return app;
    }

    private static ContentHttpResult ShowPage(string sessionId, FakePaymentGateway gateway, HttpContext httpContext)
    {
        var session = gateway.GetSession(sessionId);
        if (session is null)
        {
            return Html(StatusCodes.Status404NotFound, "Session not found", "<p>This checkout session does not exist (the fake provider keeps sessions in memory; a restart of the API forgets them).</p>");
        }

        var declined = httpContext.Request.Query.ContainsKey("declined");
        return Html(StatusCodes.Status200OK, "Fake checkout", RenderSession(session, declined));
    }

    private static IResult Pay(string sessionId, FakePaymentGateway gateway)
    {
        var session = gateway.Pay(sessionId);
        return session switch
        {
            null => TypedResults.NotFound(),
            { State: FakeSessionState.Paid } => TypedResults.Redirect(session.SuccessUrl),
            _ => TypedResults.Redirect(PagePath(sessionId)),
        };
    }

    private static IResult Decline(string sessionId, FakePaymentGateway gateway)
    {
        var session = gateway.Decline(sessionId);
        return session switch
        {
            null => TypedResults.NotFound(),
            { State: FakeSessionState.Open } => TypedResults.Redirect(PagePath(sessionId) + "?declined=1"),
            _ => TypedResults.Redirect(PagePath(sessionId)),
        };
    }

    /// <summary>"Cancel" only sends the browser back; like Stripe, no webhook: the order stays pending until it expires.</summary>
    private static IResult Cancel(string sessionId, FakePaymentGateway gateway)
    {
        var session = gateway.GetSession(sessionId);
        return session is null ? TypedResults.NotFound() : TypedResults.Redirect(session.CancelUrl);
    }

    /// <summary>Spec 07 §5: invalid signature or body → 400 and nothing stored; known event → 200; processing error → 500 (the exception handler), so the sender retries.</summary>
    private static async Task<Results<Ok<WebhookAck>, ProblemHttpResult>> ReceiveWebhook(
        HttpRequest request,
        FakePaymentGateway gateway,
        IPaymentWebhookProcessor processor,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        PaymentWebhookEvent webhookEvent;
        try
        {
            webhookEvent = gateway.ParseWebhook(new PaymentWebhookRequest(body, headers));
        }
        catch (Exception ex) when (ex is WebhookSignatureException or FormatException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid webhook.", detail: ex.Message);
        }

        var result = await processor.ProcessAsync(gateway.Provider, webhookEvent, cancellationToken);
        return TypedResults.Ok(new WebhookAck(webhookEvent.ProviderEventId, result));
    }

    private static string PagePath(string sessionId) => $"{FakePaymentGateway.CheckoutPath}/{Uri.EscapeDataString(sessionId)}";

    private static string RenderSession(FakeCheckoutSession session, bool declined)
    {
        var e = HtmlEncoder.Default;
        var html = new StringBuilder();
        html.Append("<table>");
        foreach (var item in session.Items)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{e.Encode(item.Title)}</td><td class=\"amount\">{Money(item.AmountMinor, item.Currency)}</td></tr>");
        }

        html.Append(CultureInfo.InvariantCulture, $"<tr class=\"total\"><td>Total</td><td class=\"amount\">{Money(session.AmountMinor, session.Currency)}</td></tr></table>");
        html.Append(CultureInfo.InvariantCulture, $"<p class=\"muted\">Session <code>{e.Encode(session.Id)}</code>, expires {session.ExpiresAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC.</p>");

        switch (session.State)
        {
            case FakeSessionState.Paid:
                html.Append(CultureInfo.InvariantCulture, $"<p class=\"ok\">Paid. The shop learns about it from a webhook in a moment.</p><p><a href=\"{e.Encode(session.SuccessUrl)}\">Back to the shop</a></p>");
                break;
            case FakeSessionState.Expired:
                html.Append(CultureInfo.InvariantCulture, $"<p class=\"error\">This session has expired and cannot be paid.</p><p><a href=\"{e.Encode(session.CancelUrl)}\">Back to the shop</a></p>");
                break;
            default:
                if (declined)
                {
                    html.Append("<p class=\"error\">The card was declined. Try again or cancel.</p>");
                }

                var action = e.Encode(PagePath(session.Id));
                html.Append(CultureInfo.InvariantCulture, $"""
                    <div class="actions">
                      <form method="post" action="{action}/pay"><button class="pay">Pay</button></form>
                      <form method="post" action="{action}/decline"><button>Decline</button></form>
                      <form method="post" action="{action}/cancel"><button class="link">Cancel</button></form>
                    </div>
                    """);
                break;
        }

        return html.ToString();
    }

    private static string Money(long minor, string currency) =>
        $"{(minor / 100m).ToString("0.00", CultureInfo.InvariantCulture)} {HtmlEncoder.Default.Encode(currency)}";

    private static ContentHttpResult Html(int statusCode, string title, string body) => TypedResults.Content(
        $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{title}} · FilmSearch fake payments</title>
          <style>
            body { font-family: system-ui, sans-serif; background: #f4f4f5; color: #18181b; margin: 0; padding: 2rem 1rem; }
            main { max-width: 28rem; margin: 0 auto; background: #fff; border-radius: 0.75rem; padding: 1.5rem; box-shadow: 0 1px 3px rgb(0 0 0 / 0.1); }
            h1 { font-size: 1.25rem; margin: 0 0 0.25rem; }
            .badge { display: inline-block; font-size: 0.75rem; background: #fef3c7; color: #92400e; border-radius: 999px; padding: 0.1rem 0.6rem; }
            table { width: 100%; border-collapse: collapse; margin: 1rem 0; }
            td { padding: 0.4rem 0; border-bottom: 1px solid #e4e4e7; }
            .amount { text-align: right; white-space: nowrap; }
            .total td { font-weight: 600; border-bottom: none; }
            .muted { color: #71717a; font-size: 0.8rem; }
            .ok { color: #15803d; } .error { color: #b91c1c; }
            .actions { display: flex; gap: 0.5rem; margin-top: 1rem; }
            button { font: inherit; padding: 0.5rem 1rem; border-radius: 0.5rem; border: 1px solid #d4d4d8; background: #fff; cursor: pointer; }
            button.pay { background: #18181b; color: #fff; border-color: #18181b; }
            button.link { border: none; text-decoration: underline; }
          </style>
        </head>
        <body>
          <main>
            <h1>{{title}}</h1>
            <span class="badge">Test mode: no real money</span>
            {{body}}
          </main>
        </body>
        </html>
        """,
        "text/html; charset=utf-8",
        Encoding.UTF8,
        statusCode);
}

/// <param name="EventId">The provider event id.</param>
/// <param name="Result">What processing did with the event.</param>
public sealed record WebhookAck(string EventId, WebhookProcessingResult Result);
