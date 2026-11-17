using System.Net.Http.Headers;
using System.Text;
using FilmSearch.AI.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FilmSearch.UnitTests.AI;

public sealed class RawHttpLoggingHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Logs_request_and_response_bodies_but_not_the_api_key()
    {
        var logger = new ListLogger<RawHttpLoggingHandler>();
        using var client = new HttpClient(new RawHttpLoggingHandler(logger) { InnerHandler = new FakeProviderHandler() });
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://local.test/v1/chat/completions")
        {
            Content = new StringContent("""{"model":"local-model","messages":[]}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "sk-secret");

        using var response = await client.SendAsync(request, Ct);

        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Hello there");
        logger.Messages.Count.ShouldBe(2);
        logger.Messages[0].ShouldContain("""{"model":"local-model","messages":[]}""");
        logger.Messages[1].ShouldContain("Hello there");
        logger.Messages.ShouldAllBe(m => !m.Contains("sk-secret"));
    }

    [Fact]
    public async Task Streaming_response_passes_through_and_is_logged_when_read()
    {
        var logger = new ListLogger<RawHttpLoggingHandler>();
        using var client = new HttpClient(new RawHttpLoggingHandler(logger)
        {
            InnerHandler = new FakeProviderHandler { Respond = FakeProviderHandler.StreamResponse },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://local.test/v1/chat/completions") { Content = new StringContent("{}") };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        logger.Messages.Count.ShouldBe(1);

        var body = await response.Content.ReadAsStringAsync(Ct);

        body.ShouldContain("data: [DONE]");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        logger.Messages.Count.ShouldBe(2);
        logger.Messages[1].ShouldContain("data: [DONE]");
    }

    [Fact]
    public void Long_bodies_are_cut()
    {
        var body = new string('x', RawHttpLoggingHandler.MaxBodyChars + 10);

        RawHttpLoggingHandler.Truncate(body).ShouldEndWith("… (10 more chars)");
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
