using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace FilmSearch.Infrastructure.Commerce;

/// <summary>
/// The resilience pipeline of payment provider calls (NFR-6, I19), instead of the standard handler of ServiceDefaults.
/// Outer to inner: total timeout (30 s) → retry → circuit breaker → attempt timeout (10 s).
/// <para>
/// A retry is safe only when repeating the request cannot do the thing twice: reads (GET, HEAD) and requests with an
/// <c>Idempotency-Key</c> header (Stripe returns the first result again for the same key). A POST without a key is never
/// retried, even after a timeout: the first attempt may have created a session. The standard handler retries every
/// method. Transient failures are connection errors, attempt timeouts, 408, 429 and 5xx; <c>Retry-After</c> wins.
/// </para>
/// The breaker opens for 30 s when half of at least 5 attempts in a minute failed: checkout then fails at once with
/// "payment provider unavailable" instead of every buyer waiting for the timeouts.
/// </summary>
public static class PaymentHttpResilience
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> pipeline)
    {
        pipeline.AddTimeout(TimeSpan.FromSeconds(30));
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            ShouldHandle = args => ValueTask.FromResult(
                HttpClientResiliencePredicates.IsTransient(args.Outcome) && IsSafeToRetry(args.Context.GetRequestMessage())),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldRetryAfterHeader = true,
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromMinutes(1),
            BreakDuration = TimeSpan.FromSeconds(30),
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(10));
    }

    /// <summary>Repeating the request cannot have a second effect.</summary>
    public static bool IsSafeToRetry(HttpRequestMessage? request) =>
        request is not null
        && (request.Method == HttpMethod.Get
            || request.Method == HttpMethod.Head
            || request.Headers.Contains(IdempotencyKeyHeader));
}
