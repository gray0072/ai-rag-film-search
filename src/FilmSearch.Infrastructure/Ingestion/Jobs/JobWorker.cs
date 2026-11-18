using System.Diagnostics;
using System.Text.Json;
using FilmSearch.Core.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FilmSearch.Infrastructure.Ingestion.Jobs;

/// <summary>Runs one kind of job. Registered as a keyed scoped service under its <see cref="JobKind"/>.</summary>
internal interface IJobHandler
{
    /// <summary>Returns the result to store (serialized as JSON), or null.</summary>
    /// <exception cref="JobFailedException">The job failed; its <see cref="JobFailedException.Result"/> is stored too.</exception>
    Task<object?> RunAsync(JobStatus job, JobProgress progress, CancellationToken cancellationToken);
}

/// <summary>A failure with a partial result worth keeping (e.g. the movies saved before the embeddings failed).</summary>
internal sealed class JobFailedException(string message, object? result, Exception? innerException = null)
    : Exception(message, innerException)
{
    public object? Result { get; } = result;
}

/// <summary>The latest progress of a running job, written to the database with the next heartbeat.</summary>
internal sealed class JobProgress
{
    private string? _json;

    public string? Json => Volatile.Read(ref _json);

    public void Report(object progress) => Volatile.Write(ref _json, JsonSerializer.Serialize(progress, progress.GetType(), JobJson.Options));

    /// <summary>An <see cref="IProgress{T}"/> that reports on the calling thread (<see cref="Progress{T}"/> would post to the thread pool, out of order).</summary>
    public IProgress<T> For<T>(Func<T, object>? map = null)
        where T : notnull => new Reporter<T>(this, map);

    private sealed class Reporter<T>(JobProgress owner, Func<T, object>? map) : IProgress<T>
        where T : notnull
    {
        public void Report(T value) => owner.Report(map is null ? value : map(value));
    }
}

/// <summary>
/// The Worker's job loop (spec 08 §3): claims the next job from <see cref="JobStore"/>, runs its handler with a heartbeat
/// beside it, and stores the outcome. One job at a time per process; more Worker replicas share the queue through
/// <c>SKIP LOCKED</c>. On shutdown a running job is put back in the queue; a cancel request or a lost lease stops it at the
/// next heartbeat.
/// </summary>
internal sealed partial class JobWorker(
    IServiceScopeFactory scopes,
    IOptions<JobOptions> options,
    TimeProvider time,
    ILogger<JobWorker> logger) : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("FilmSearch");

    public string WorkerId { get; } = $"{Environment.MachineName}/{Environment.ProcessId}/{Guid.NewGuid().ToString("N")[..6]}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger, WorkerId);
        var poll = TimeSpan.FromMilliseconds(options.Value.PollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            ClaimedJob? claimed = null;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                claimed = await scope.ServiceProvider.GetRequiredService<JobStore>().ClaimNextAsync(WorkerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Database down or not migrated yet (the API applies migrations): try again after the poll interval.
                LogClaimFailed(logger, ex);
            }

            if (claimed is not null)
            {
                await RunAsync(claimed, stoppingToken);
                continue;
            }

            try
            {
                await Task.Delay(poll, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunAsync(ClaimedJob claimed, CancellationToken stoppingToken)
    {
        var job = claimed.Job;
        using var activity = ActivitySource.StartActivity("jobs.run");
        activity?.SetTag("filmsearch.job.id", job.Id);
        activity?.SetTag("filmsearch.job.kind", job.Kind.ToString());
        activity?.SetTag("filmsearch.job.trigger", job.Trigger.ToString());
        activity?.SetTag("filmsearch.job.attempt", job.Attempts);
        LogRunning(logger, job.Kind, job.Id, job.Attempts);
        var stopwatch = Stopwatch.StartNew();

        var progress = new JobProgress();
        using var run = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var heartbeatStop = new CancellationTokenSource();
        var heartbeat = new Heartbeat();
        var heartbeatLoop = HeartbeatLoopAsync(claimed, progress, heartbeat, run, heartbeatStop.Token);

        JobState state;
        object? result = null;
        string? error = null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredKeyedService<IJobHandler>(job.Kind);
            result = await handler.RunAsync(job, progress, run.Token);
            state = JobState.Succeeded;
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            state = JobState.Cancelled;
        }
        catch (JobFailedException ex)
        {
            (state, result, error) = (JobState.Failed, ex.Result, ex.Message);
            LogFailed(logger, ex, job.Kind, job.Id);
        }
        catch (Exception ex)
        {
            (state, error) = (JobState.Failed, ex.Message);
            LogFailed(logger, ex, job.Kind, job.Id);
        }
        finally
        {
            await heartbeatStop.CancelAsync();
            await heartbeatLoop;
        }

        activity?.SetTag("filmsearch.job.state", state.ToString());
        await using (var scope = scopes.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<JobStore>();
            if (state == JobState.Cancelled && stoppingToken.IsCancellationRequested)
            {
                await store.ReleaseAsync(job.Id, claimed.LeaseId, CancellationToken.None);
                LogReleased(logger, job.Kind, job.Id);
                return;
            }

            if (heartbeat.Outcome == HeartbeatOutcome.LeaseLost)
            {
                LogLeaseLost(logger, job.Kind, job.Id);
                return;
            }

            var resultJson = result is null ? null : JsonSerializer.Serialize(result, result.GetType(), JobJson.Options);
            if (!await store.FinishAsync(job.Id, claimed.LeaseId, state, progress.Json, resultJson, error, CancellationToken.None))
            {
                LogLeaseLost(logger, job.Kind, job.Id);
                return;
            }
        }

        LogFinished(logger, job.Kind, job.Id, state, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Writes progress and the heartbeat every interval; cancels the run on a cancel request or a lost lease.</summary>
    private async Task HeartbeatLoopAsync(
        ClaimedJob claimed,
        JobProgress progress,
        Heartbeat heartbeat,
        CancellationTokenSource run,
        CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.HeartbeatIntervalMs), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var outcome = await scope.ServiceProvider.GetRequiredService<JobStore>()
                        .HeartbeatAsync(claimed.Job.Id, claimed.LeaseId, progress.Json, stop);
                    if (outcome != HeartbeatOutcome.Continue)
                    {
                        heartbeat.Outcome = outcome;
                        await run.CancelAsync();
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A missed heartbeat is fine; the lease is many intervals long.
                    LogHeartbeatFailed(logger, ex, claimed.Job.Id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The run ended.
        }
    }

    private sealed class Heartbeat
    {
        public HeartbeatOutcome Outcome { get; set; } = HeartbeatOutcome.Continue;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job worker {WorkerId} started")]
    private static partial void LogStarted(ILogger logger, string workerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not claim a job; retrying after the poll interval")]
    private static partial void LogClaimFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running {Kind} job {JobId} (attempt {Attempt})")]
    private static partial void LogRunning(ILogger logger, JobKind kind, Guid jobId, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} job {JobId} finished: {State} in {ElapsedMs} ms")]
    private static partial void LogFinished(ILogger logger, JobKind kind, Guid jobId, JobState state, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Kind} job {JobId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, JobKind kind, Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} job {JobId} put back in the queue: the worker is stopping")]
    private static partial void LogReleased(ILogger logger, JobKind kind, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Kind} job {JobId}: the lease was lost (heartbeats too late); another worker owns the job now")]
    private static partial void LogLeaseLost(ILogger logger, JobKind kind, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Heartbeat of job {JobId} failed")]
    private static partial void LogHeartbeatFailed(ILogger logger, Exception exception, Guid jobId);
}
