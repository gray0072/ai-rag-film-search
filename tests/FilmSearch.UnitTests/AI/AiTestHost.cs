using System.Net;
using System.Text;
using FilmSearch.AI.Health;
using FilmSearch.AI.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FilmSearch.UnitTests.AI;

/// <summary>
/// A host with the real AI registration (<see cref="AiServiceCollectionExtensions.AddAiProviders"/>) whose HTTP goes to
/// a <see cref="FakeProviderHandler"/>: the OpenAI client, the Microsoft.Extensions.AI pipeline and the JSON on the
/// wire are all real, but no LM Studio or network is needed.
/// </summary>
internal static class AiTestHost
{
    public static readonly Dictionary<string, string?> BaseConfig = new()
    {
        ["AI:ActiveChatProfile"] = "local",
        ["AI:ActiveEmbeddingProfile"] = "local",
        ["AI:ChatProfiles:local:Endpoint"] = "http://local.test/v1",
        ["AI:ChatProfiles:local:Model"] = "local-model",
        ["AI:ChatProfiles:local:ApiKey"] = "lm-studio",
        ["AI:ChatProfiles:local:Temperature"] = "0.3",
        ["AI:ChatProfiles:local:MaxOutputTokens"] = "256",
        ["AI:ChatProfiles:local:ReasoningEffort"] = "None",
        ["AI:ChatProfiles:local:MaxRetries"] = "0",
        ["AI:ChatProfiles:openrouter:Endpoint"] = "https://router.test/api/v1",
        ["AI:ChatProfiles:openrouter:Model"] = "vendor/cheap-model",
        ["AI:ChatProfiles:openrouter:ApiKey"] = "secret-key",
        ["AI:ChatProfiles:openrouter:MaxRetries"] = "0",
        ["AI:EmbeddingProfiles:local:Endpoint"] = "http://local.test/v1",
        ["AI:EmbeddingProfiles:local:Model"] = "embedding-model",
        ["AI:EmbeddingProfiles:local:ApiKey"] = "lm-studio",
        ["AI:EmbeddingProfiles:local:Dimensions"] = "3",
        ["AI:EmbeddingProfiles:local:MaxRetries"] = "0",
        ["AI:Pricing:vendor/cheap-model:InputPerMTokUsd"] = "1",
        ["AI:Pricing:vendor/cheap-model:OutputPerMTokUsd"] = "2",
    };

    public static IHost Build(
        FakeProviderHandler handler,
        Dictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(BaseConfig);
        builder.Configuration.AddInMemoryCollection(overrides ?? []);
        builder.Services.AddLogging();
        builder.Services.AddHealthChecks(); // as ServiceDefaults does in the API
        builder.AddAiProviders();
        builder.Services.AddHttpClient(AiClientFactory.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        builder.Services.AddHttpClient(AiProviderHealthCheck.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        configureServices?.Invoke(builder.Services);
        return builder.Build();
    }
}

/// <summary>Answers like an OpenAI-compatible provider and records the requests.</summary>
internal sealed class FakeProviderHandler : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = DefaultResponse;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
        return Respond(request);
    }

    public static HttpResponseMessage DefaultResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/models", StringComparison.Ordinal))
        {
            return Json("""{"object":"list","data":[{"id":"local-model","object":"model"},{"id":"embedding-model","object":"model"}]}""");
        }

        if (path.EndsWith("/embeddings", StringComparison.Ordinal))
        {
            return Json("""
                {"object":"list","model":"embedding-model","usage":{"prompt_tokens":5,"total_tokens":5},
                 "data":[{"object":"embedding","index":0,"embedding":[0.1,0.2,0.3]}]}
                """);
        }

        return Json("""
            {"id":"c1","object":"chat.completion","created":1,"model":"served-model",
             "choices":[{"index":0,"message":{"role":"assistant","content":"Hello there"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":100,"completion_tokens":50,"total_tokens":150}}
            """);
    }

    public static HttpResponseMessage StreamResponse(HttpRequestMessage request) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            """
            data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"served-model","choices":[{"index":0,"delta":{"role":"assistant","content":"Hel"},"finish_reason":null}]}

            data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"served-model","choices":[{"index":0,"delta":{"content":"lo"},"finish_reason":"stop"}]}

            data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"served-model","choices":[],"usage":{"prompt_tokens":100,"completion_tokens":50,"total_tokens":150}}

            data: [DONE]


            """,
            Encoding.UTF8,
            "text/event-stream"),
    };

    /// <summary>A chat completion whose message content is <paramref name="content"/>.</summary>
    public static HttpResponseMessage ChatResponse(string content) => Json($$$"""
        {"id":"c1","object":"chat.completion","created":1,"model":"served-model",
         "choices":[{"index":0,"message":{"role":"assistant","content":{{{System.Text.Json.JsonSerializer.Serialize(content)}}}},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":100,"completion_tokens":50,"total_tokens":150}}
        """);

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body);
