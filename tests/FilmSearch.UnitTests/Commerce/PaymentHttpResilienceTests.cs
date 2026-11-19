using System.Net;
using FilmSearch.Infrastructure.Commerce;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.UnitTests.Commerce;

/// <summary>Payment calls are retried only when a repeat cannot pay or create twice (I19).</summary>
public sealed class PaymentHttpResilienceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("GET", false, 3)]
    [InlineData("POST", true, 3)]
    [InlineData("POST", false, 1)]
    public async Task Only_reads_and_idempotent_requests_are_retried(string method, bool idempotencyKey, int expectedAttempts)
    {
        var handler = new UnavailableHandler();
        var services = new ServiceCollection();
        services.AddHttpClient("payments-test")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler("payments", PaymentHttpResilience.Configure);
        await using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("payments-test");
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://payments.test/v1/checkout/sessions");
        if (idempotencyKey)
        {
            request.Headers.Add(PaymentHttpResilience.IdempotencyKeyHeader, "order-1");
        }

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        handler.Attempts.ShouldBe(expectedAttempts);
    }

    /// <summary>Always 503 with <c>Retry-After: 0</c>, so retries do not wait.</summary>
    private sealed class UnavailableHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }
}
