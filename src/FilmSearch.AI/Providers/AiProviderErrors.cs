using System.ClientModel;
using Polly;

namespace FilmSearch.AI.Providers;

/// <summary>Recognizes exceptions that mean "the AI provider is down or failed", as opposed to bugs in our code.</summary>
public static class AiProviderErrors
{
    /// <summary>
    /// True for HTTP and SDK errors, timeouts (a cancellation the caller did not ask for), and the
    /// <see cref="AggregateException"/> the OpenAI client throws when every retry failed ("Retry failed after N tries"),
    /// and the rejections of the HTTP resilience pipeline (I19): an open circuit, an attempt timeout.
    /// </summary>
    public static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            HttpRequestException or ClientResultException or TimeoutException or ExecutionRejectedException => true,
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            AggregateException aggregate => aggregate.InnerExceptions.Count > 0
                && aggregate.InnerExceptions.All(e => IsProviderFailure(e, cancellationToken)),
            _ => false,
        };
}
