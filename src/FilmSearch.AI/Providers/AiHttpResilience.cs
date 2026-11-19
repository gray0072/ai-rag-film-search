using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;

namespace FilmSearch.AI.Providers;

/// <summary>
/// The resilience pipeline of one AI profile's HTTP client (NFR-6, I19), outer to inner:
/// <list type="number">
/// <item>retry — only failures where the model did not start working: connection errors, 429, 502, 503, 504, with
/// exponential backoff and jitter; <c>Retry-After</c> wins. Timeouts and 500 are not retried: a local model that timed
/// out is most likely still generating (LM Studio keeps the request in its queue), so a second request would double the
/// load and the wait. A response that has started streaming is never retried (the handler is done by then);</item>
/// <item>circuit breaker — opens for <see cref="AiProfileOptions.CircuitBreakSeconds"/> when half of at least
/// <see cref="MinimumThroughput"/> attempts in <see cref="SamplingDuration"/> failed (connection errors, timeouts, 408,
/// 429, 5xx). While open, calls fail at once with <see cref="Polly.CircuitBreaker.BrokenCircuitException"/>, so the
/// fallback profile answers without waiting for another timeout;</item>
/// <item>attempt timeout — <see cref="AiProfileOptions.TimeoutSeconds"/> until the response headers (for a non-streamed
/// answer of a local model that is the whole generation).</item>
/// </list>
/// One pipeline per profile (one named HTTP client each), so a slow chat model does not open the breaker of the embedding
/// model on the same LM Studio server. The OpenAI SDK's own retries are off; its network timeout stays as a backstop for
/// reading a streamed body (<see cref="SdkTimeoutMargin"/> longer than the attempt timeout, so the attempt timeout fires
/// first and the breaker counts it).
/// </summary>
internal static class AiHttpResilience
{
    public const int MinimumThroughput = 3;
    public const double FailureRatio = 0.5;
    public static readonly TimeSpan SamplingDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan SdkTimeoutMargin = TimeSpan.FromSeconds(10);

    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> pipeline, AiProfileOptions profile)
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            // Polly needs at least one attempt in the options; zero retries is a predicate that never matches.
            MaxRetryAttempts = Math.Max(profile.MaxRetries, 1),
            ShouldHandle = profile.MaxRetries == 0
                ? _ => PredicateResult.False()
                : args => ValueTask.FromResult(IsRetryable(args.Outcome)),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(profile.RetryDelayMs),
            MaxDelay = TimeSpan.FromSeconds(30),
            ShouldRetryAfterHeader = true,
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = FailureRatio,
            MinimumThroughput = MinimumThroughput,
            SamplingDuration = SamplingDuration,
            BreakDuration = TimeSpan.FromSeconds(profile.CircuitBreakSeconds),
            ShouldHandle = args => ValueTask.FromResult(IsFailure(args.Outcome)),
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(profile.TimeoutSeconds));
    }

    /// <summary>The request did not reach the model, or the provider refused it before working on it.</summary>
    internal static bool IsRetryable(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Exception: HttpRequestException } => true,
        { Result.StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout } => true,
        _ => false,
    };

    /// <summary>What counts against the circuit breaker: the provider is down, overloaded or too slow.</summary>
    internal static bool IsFailure(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Exception: HttpRequestException or TimeoutRejectedException } => true,
        { Result: { } response } => (int)response.StatusCode >= 500
            || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
        _ => false,
    };
}
