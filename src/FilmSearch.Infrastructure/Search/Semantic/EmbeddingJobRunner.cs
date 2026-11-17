using System.Threading.Channels;
using FilmSearch.Core.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FilmSearch.Infrastructure.Search.Semantic;

/// <summary>
/// Runs reindex jobs in the API process, one at a time, until the Worker takes them over (I16).
/// The state lives in memory: a restart forgets the last job, and the next reindex finds the stale vectors by hash anyway.
/// </summary>
internal sealed partial class EmbeddingJobRunner(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<EmbeddingJobRunner> logger) : BackgroundService, IEmbeddingJobs
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Lock _lock = new();
    private EmbeddingJobStatus? _current;

    public EmbeddingJobStatus? Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public bool TryStart(ReindexRequest request, out EmbeddingJobStatus status)
    {
        lock (_lock)
        {
            if (_current is { IsActive: true })
            {
                status = _current;
                return false;
            }

            var now = time.GetUtcNow();
            _current = new EmbeddingJobStatus
            {
                Id = Guid.CreateVersion7(now),
                State = EmbeddingJobState.Queued,
                Request = request,
                QueuedAt = now,
            };
            _queue.Writer.TryWrite(_current.Id);
            status = _current;
            return true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var jobId in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await RunAsync(jobId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    private async Task RunAsync(Guid jobId, CancellationToken stoppingToken)
    {
        var job = Update(jobId, s => s with { State = EmbeddingJobState.Running, StartedAt = time.GetUtcNow() });
        if (job is null)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var indexer = scope.ServiceProvider.GetRequiredService<IMovieEmbeddingIndexer>();
            var progress = new SyncProgress(p => Update(jobId, s => s with { Progress = p }));

            var result = await indexer.ReindexAsync(job.Request, progress, stoppingToken);
            Update(jobId, s => s with { State = EmbeddingJobState.Succeeded, Result = result, FinishedAt = time.GetUtcNow() });
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Update(jobId, s => s with { State = EmbeddingJobState.Cancelled, FinishedAt = time.GetUtcNow() });
            throw;
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex, jobId);
            Update(jobId, s => s with { State = EmbeddingJobState.Failed, Error = ex.Message, FinishedAt = time.GetUtcNow() });
        }
    }

    private EmbeddingJobStatus? Update(Guid jobId, Func<EmbeddingJobStatus, EmbeddingJobStatus> change)
    {
        lock (_lock)
        {
            if (_current?.Id != jobId)
            {
                return null;
            }

            _current = change(_current);
            return _current;
        }
    }

    /// <summary>Reports on the calling thread (<see cref="Progress{T}"/> would post to the thread pool, out of order).</summary>
    private sealed class SyncProgress(Action<ReindexProgress> report) : IProgress<ReindexProgress>
    {
        public void Report(ReindexProgress value) => report(value);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Embedding reindex job {JobId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid jobId);
}
