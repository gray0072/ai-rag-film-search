using System.ClientModel;

namespace FilmSearch.AI.Providers;

/// <summary>Recognizes exceptions that mean "the AI provider is down or failed", as opposed to bugs in our code.</summary>
public static class AiProviderErrors
{
    /// <summary>
    /// True for HTTP and SDK errors, timeouts (a cancellation the caller did not ask for), and the
    /// <see cref="AggregateException"/> the OpenAI client throws when every retry failed ("Retry failed after N tries").
    /// </summary>
    public static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            HttpRequestException or ClientResultException or TimeoutException => true,
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            AggregateException aggregate => aggregate.InnerExceptions.Count > 0
                && aggregate.InnerExceptions.All(e => IsProviderFailure(e, cancellationToken)),
            _ => false,
        };
}
