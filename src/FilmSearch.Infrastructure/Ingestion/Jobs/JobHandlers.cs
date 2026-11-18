using FilmSearch.Core.Ingestion;
using FilmSearch.Core.Search;

namespace FilmSearch.Infrastructure.Ingestion.Jobs;

/// <summary>A reindex of the embedding index (spec 08 §4), queued by the admin endpoint or by the API on startup.</summary>
internal sealed class EmbeddingReindexJobHandler(IMovieEmbeddingIndexer indexer) : IJobHandler
{
    public async Task<object?> RunAsync(JobStatus job, JobProgress progress, CancellationToken cancellationToken) =>
        await indexer.ReindexAsync(job.GetParameters<ReindexRequest>(), progress.For<ReindexProgress>(), cancellationToken);
}

/// <summary>A TMDB import (FR-DATA-2), queued by the admin endpoint or by the daily schedule.</summary>
internal sealed class TmdbImportJobHandler(ITmdbImporter importer) : IJobHandler
{
    public async Task<object?> RunAsync(JobStatus job, JobProgress progress, CancellationToken cancellationToken)
    {
        try
        {
            return await importer.ImportAsync(job.GetParameters<TmdbImportRequest>(), progress.For<TmdbImportProgress>(), cancellationToken);
        }
        catch (TmdbImportException ex)
        {
            throw new JobFailedException(ex.Message, ex.Result, ex);
        }
    }
}

/// <summary><see cref="IEmbeddingJobs"/> over the job queue: reindex jobs are rows of kind <see cref="JobKind.EmbeddingReindex"/>.</summary>
internal sealed class EmbeddingJobs(IBackgroundJobs queue) : IEmbeddingJobs
{
    public async Task<EmbeddingJobStatus?> GetLatestAsync(CancellationToken cancellationToken) =>
        (await queue.ListAsync(JobKind.EmbeddingReindex, 1, cancellationToken)) is [var job] ? ToStatus(job) : null;

    public async Task<(bool Started, EmbeddingJobStatus Status)> TryStartAsync(
        ReindexRequest request,
        JobTrigger trigger,
        CancellationToken cancellationToken)
    {
        var (created, job) = await queue.EnqueueAsync(JobKind.EmbeddingReindex, request, trigger, cancellationToken);
        return (created, ToStatus(job));
    }

    private static EmbeddingJobStatus ToStatus(JobStatus job) => new()
    {
        Id = job.Id,
        State = job.State,
        Trigger = job.Trigger,
        Request = job.GetParameters<ReindexRequest>(),
        QueuedAt = job.QueuedAt,
        StartedAt = job.StartedAt,
        FinishedAt = job.FinishedAt,
        Progress = job.GetProgress<ReindexProgress>(),
        Result = job.GetResult<ReindexResult>(),
        Error = job.Error,
    };
}
