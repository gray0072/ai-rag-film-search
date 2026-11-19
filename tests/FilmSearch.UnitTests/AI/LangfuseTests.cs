using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using FilmSearch.AI.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace FilmSearch.UnitTests.AI;

/// <summary>I18: the optional Langfuse export (trace filter, exporter options) and feedback scores.</summary>
public sealed class LangfuseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_trace_filter_passes_on_traces_with_ai_work_from_the_ai_spans_up_to_the_root()
    {
        var sourceSuffix = Guid.NewGuid().ToString("N");
        using var http = new ActivitySource($"Test.Http.{sourceSuffix}");
        using var db = new ActivitySource($"Test.Npgsql.{sourceSuffix}");
        using var ai = new ActivitySource("FilmSearch.AI");
        using var listener = Listen(http.Name, db.Name, ai.Name);
        var exported = new Recording();
        using var filter = new AiTraceFilter(exported);

        // A chat request: a query before the turn, the turn with a tool and its query, the request span last.
        using (var request = http.StartActivity("POST /api/chat")!)
        {
            End(filter, db.StartActivity("SELECT conversation")!);
            using (var turn = ai.StartActivity("invoke_agent assistant")!)
            {
                using (var tool = ai.StartActivity("execute_tool search_movies")!)
                {
                    End(filter, db.StartActivity("SELECT movies")!);
                    End(filter, tool);
                }

                End(filter, turn);
            }

            End(filter, request);
        }

        // A request without AI work.
        using (var health = http.StartActivity("GET /api/movies")!)
        {
            End(filter, db.StartActivity("SELECT movies page")!);
            End(filter, health);
        }

        exported.Names.ShouldBe(["SELECT movies", "execute_tool search_movies", "invoke_agent assistant", "POST /api/chat"]);
    }

    [Fact]
    public void Langfuse_export_is_off_without_complete_settings_and_targets_the_otlp_endpoint_with_basic_auth()
    {
        LangfuseExport.CreateOptions(Config(new() { [LangfuseExport.BaseUrlKey] = "http://localhost:3000" })).ShouldBeNull();

        var options = LangfuseExport.CreateOptions(Config(new()
        {
            [LangfuseExport.BaseUrlKey] = "http://localhost:3000/",
            [LangfuseExport.PublicKeyKey] = "pk-lf-a",
            [LangfuseExport.SecretKeyKey] = "sk-lf-b",
        })).ShouldNotBeNull();

        options.Endpoint.ShouldBe(new Uri("http://localhost:3000/api/public/otel/v1/traces"));
        options.Protocol.ShouldBe(OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf);
        options.Headers.ShouldBe($"Authorization=Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("pk-lf-a:sk-lf-b"))}");
    }

    [Fact]
    public async Task A_rating_is_sent_as_a_numeric_score_of_the_turn_trace_with_an_id_per_answer()
    {
        var handler = new CapturingHandler();
        var sender = new LangfuseScoreSender(
            new LangfuseScoreQueue(),
            new SingleClientFactory(handler),
            Options.Create(new LangfuseOptions { BaseUrl = "http://langfuse:3000", PublicKey = "pk-lf-1", SecretKey = "sk-lf-2" }),
            NullLogger<LangfuseScoreSender>.Instance);
        var messageId = Guid.CreateVersion7();

        await sender.SendAsync(new FeedbackScore("0af7651916cd43dd8448eb211c80319c", messageId, -1, "too vague"), Ct);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.ShouldBe(new Uri("http://langfuse:3000/api/public/scores"));
        request.Authorization.ShouldBe($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("pk-lf-1:sk-lf-2"))}");
        var body = JsonDocument.Parse(request.Body).RootElement;
        body.GetProperty("id").GetString().ShouldBe($"feedback-{messageId}");
        body.GetProperty("traceId").GetString().ShouldBe("0af7651916cd43dd8448eb211c80319c");
        body.GetProperty("name").GetString().ShouldBe(LangfuseScoreSender.ScoreName);
        body.GetProperty("value").GetInt32().ShouldBe(-1);
        body.GetProperty("dataType").GetString().ShouldBe("NUMERIC");
        body.GetProperty("comment").GetString().ShouldBe("too vague");
    }

    [Fact]
    public void Without_langfuse_settings_scores_go_nowhere()
    {
        var services = new ServiceCollection().AddLangfuseScores(Config([])).BuildServiceProvider();

        services.GetRequiredService<IFeedbackScoreSink>().ShouldBeOfType<NoFeedbackScoreSink>();
        services.GetServices<IHostedService>().ShouldBeEmpty();
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ActivityListener Listen(params string[] sources)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => sources.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    /// <summary>What the tracer provider does when a span ends: stop it, then hand it to the processor.</summary>
    private static void End(AiTraceFilter filter, Activity activity)
    {
        activity.Stop();
        filter.OnEnd(activity);
    }

    private sealed class Recording : BaseProcessor<Activity>
    {
        public List<string> Names { get; } = [];

        public override void OnEnd(Activity data) => Names.Add(data.OperationName);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(Uri Uri, string? Authorization, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString(), await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
