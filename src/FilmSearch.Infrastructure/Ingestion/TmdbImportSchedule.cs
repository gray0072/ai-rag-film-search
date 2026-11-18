using FilmSearch.Core.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FilmSearch.Infrastructure.Ingestion;

/// <summary>Configuration section <c>Tmdb:Schedule</c>: a daily TMDB import (spec 08 §3), off by default.</summary>
public sealed class TmdbScheduleOptions
{
    public const string SectionName = "Tmdb:Schedule";

    public bool Enabled { get; set; }

    /// <summary>Time of day (UTC) after which the day's import is queued.</summary>
    public TimeOnly TimeUtc { get; set; } = new(3, 0);

    /// <summary>The daily import: new hits come in through the popular list.</summary>
    public TmdbMovieList List { get; set; } = TmdbMovieList.Popular;

    public int Pages { get; set; } = 5;
}

/// <summary>
/// Queues the day's TMDB import once the configured time has passed. Stateless and restart-safe: it asks the job table
/// whether a scheduled import was queued since today's time, so a restarted or second Worker does not queue it again
/// (and one active import per kind guards the rest).
/// </summary>
internal sealed partial class TmdbImportScheduler(
    IServiceScopeFactory scopes,
    IOptions<TmdbScheduleOptions> options,
    TimeProvider time,
    ILogger<TmdbImportScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        LogEnabled(logger, settings.TimeUtc, settings.List, settings.Pages);
        using var timer = new PeriodicTimer(CheckInterval, time);
        try
        {
            do
            {
                try
                {
                    await QueueIfDueAsync(settings, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogCheckFailed(logger, ex);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    internal async Task QueueIfDueAsync(TmdbScheduleOptions settings, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var dueAt = new DateTimeOffset(DateOnly.FromDateTime(now.UtcDateTime), settings.TimeUtc, TimeSpan.Zero);
        if (now < dueAt)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IBackgroundJobs>();
        if (await queue.AnyQueuedSinceAsync(JobKind.TmdbImport, JobTrigger.Schedule, dueAt, cancellationToken))
        {
            return;
        }

        var (created, job) = await queue.EnqueueAsync(
            JobKind.TmdbImport,
            new TmdbImportRequest(settings.List, settings.Pages),
            JobTrigger.Schedule,
            cancellationToken);
        if (created)
        {
            LogQueued(logger, job.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Daily TMDB import at {TimeUtc} UTC: {List}, {Pages} page(s)")]
    private static partial void LogEnabled(ILogger logger, TimeOnly timeUtc, TmdbMovieList list, int pages);

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued the daily TMDB import {JobId}")]
    private static partial void LogQueued(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not check the TMDB import schedule; the next check retries")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);
}
