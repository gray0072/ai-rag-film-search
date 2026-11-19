using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FilmSearch.AI.Telemetry;

/// <summary>
/// The optional <c>Langfuse</c> section (I18, spec 10 §3): a self-hosted Langfuse, an LLM-specific trace viewer. The AppHost
/// sets it when Langfuse is switched on. Traces go there through OTLP (ServiceDefaults reads the same keys); user
/// ratings of answers go there as scores through its public API.
/// </summary>
public sealed class LangfuseOptions
{
    public const string SectionName = "Langfuse";

    /// <summary>Base URL of Langfuse, e.g. <c>http://localhost:3000</c>. Empty = Langfuse is off.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Project public key (<c>pk-lf-…</c>).</summary>
    public string PublicKey { get; set; } = "";

    /// <summary>Project secret key (<c>sk-lf-…</c>), from the AppHost parameters or user secrets.</summary>
    public string SecretKey { get; set; } = "";

    public bool Enabled =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>HTTP Basic credentials of the public API and the OTLP endpoint.</summary>
    public string BasicAuth() => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{PublicKey}:{SecretKey}"));
}

/// <summary>A user rating of an answer, attached to the trace of the turn that wrote it.</summary>
/// <param name="TraceId">Trace id of the turn (32 hex characters).</param>
/// <param name="MessageId">The rated answer; also makes the score id, so a changed rating replaces the old score.</param>
/// <param name="Rating">1 (thumbs up) or -1 (thumbs down).</param>
/// <param name="Comment">Optional comment of the user.</param>
public sealed record FeedbackScore(string TraceId, Guid MessageId, int Rating, string? Comment);

/// <summary>Where user ratings go besides the database. Never blocks and never throws.</summary>
public interface IFeedbackScoreSink
{
    void Enqueue(FeedbackScore score);
}

internal sealed class NoFeedbackScoreSink : IFeedbackScoreSink
{
    public void Enqueue(FeedbackScore score)
    {
    }
}

/// <summary>Scores wait in memory and are sent by <see cref="LangfuseScoreSender"/>: the feedback request does not wait for Langfuse.</summary>
internal sealed class LangfuseScoreQueue : IFeedbackScoreSink
{
    private const int Capacity = 1000;

    private readonly Channel<FeedbackScore> _channel = Channel.CreateBounded<FeedbackScore>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<FeedbackScore> Reader => _channel.Reader;

    public void Enqueue(FeedbackScore score) => _channel.Writer.TryWrite(score);
}

/// <summary>Sends queued scores to <c>POST /api/public/scores</c>. A failure is logged and the score is dropped: scores are a convenience, the rating is in the database.</summary>
internal sealed partial class LangfuseScoreSender(
    LangfuseScoreQueue queue,
    IHttpClientFactory httpClientFactory,
    IOptions<LangfuseOptions> options,
    ILogger<LangfuseScoreSender> logger) : BackgroundService
{
    internal const string HttpClientName = "Langfuse";

    /// <summary>Name of the score in Langfuse.</summary>
    internal const string ScoreName = "user_feedback";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var score in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await SendAsync(score, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                LogFailed(logger, score.MessageId, ex);
            }
        }
    }

    internal async Task SendAsync(FeedbackScore score, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.BaseUrl.TrimEnd('/') + "/"), "api/public/scores"))
        {
            Content = JsonContent.Create(new
            {
                id = $"feedback-{score.MessageId}",
                traceId = score.TraceId,
                name = ScoreName,
                value = score.Rating,
                dataType = "NUMERIC",
                comment = score.Comment,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", settings.BasicAuth());

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        LogSent(logger, score.MessageId, score.TraceId);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Feedback on answer {MessageId} sent to Langfuse as a score of trace {TraceId}")]
    private static partial void LogSent(ILogger logger, Guid messageId, string traceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Feedback on answer {MessageId} could not be sent to Langfuse")]
    private static partial void LogFailed(ILogger logger, Guid messageId, Exception exception);
}

public static class LangfuseServiceCollectionExtensions
{
    /// <summary>
    /// <see cref="IFeedbackScoreSink"/>: Langfuse scores when the <c>Langfuse</c> section is complete, otherwise a no-op.
    /// </summary>
    public static IServiceCollection AddLangfuseScores(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(LangfuseOptions.SectionName);
        services.AddOptions<LangfuseOptions>().Bind(section);

        var settings = section.Get<LangfuseOptions>() ?? new LangfuseOptions();
        if (!settings.Enabled)
        {
            services.AddSingleton<IFeedbackScoreSink, NoFeedbackScoreSink>();
            return services;
        }

        services.AddHttpClient(LangfuseScoreSender.HttpClientName);
        services.AddSingleton<LangfuseScoreQueue>();
        services.AddSingleton<IFeedbackScoreSink>(sp => sp.GetRequiredService<LangfuseScoreQueue>());
        services.AddHostedService<LangfuseScoreSender>();
        return services;
    }
}
