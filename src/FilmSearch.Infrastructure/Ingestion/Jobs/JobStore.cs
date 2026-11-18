using System.Text.Json;
using FilmSearch.Core.Ingestion;
using FilmSearch.Infrastructure.Persistence;
using FilmSearch.Infrastructure.Persistence.Configurations.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FilmSearch.Infrastructure.Ingestion.Jobs;

/// <summary>Configuration section <c>Jobs</c>: how the Worker polls and holds jobs.</summary>
public sealed class JobOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Delay between two looks at the queue while it is empty.</summary>
    public int PollIntervalMs { get; set; } = 2000;

    /// <summary>How often a running job writes its progress and heartbeat.</summary>
    public int HeartbeatIntervalMs { get; set; } = 2000;

    /// <summary>A running job without a heartbeat for this long belongs to a lost worker: another worker takes it.</summary>
    public int LeaseSeconds { get; set; } = 60;

    /// <summary>Starts of a job (lost workers included) before it is failed.</summary>
    public int MaxAttempts { get; set; } = 3;
}

/// <summary>A job claimed by a worker: the lease id must be passed back with every heartbeat and the outcome.</summary>
internal sealed record ClaimedJob(JobStatus Job, Guid LeaseId);

/// <summary>Outcome of a heartbeat: the job goes on, the admin asked to cancel it, or the worker lost its lease.</summary>
internal enum HeartbeatOutcome
{
    Continue,
    CancelRequested,
    LeaseLost,
}

/// <summary>
/// The job table as a queue (spec 08 §3). The API enqueues, the Worker claims the oldest queued job with
/// <c>FOR UPDATE SKIP LOCKED</c> (two workers never take the same row and never wait for each other), runs it and writes
/// heartbeats. A running job whose heartbeat is older than the lease is claimed again by any worker, up to
/// <see cref="JobOptions.MaxAttempts"/>: the jobs are idempotent (upsert by TMDB id, embeddings by content hash).
/// </summary>
internal sealed class JobStore(AppDbContext db, TimeProvider time, IOptions<JobOptions> options) : IBackgroundJobs
{
    public const int MaxErrorLength = 4000;

    public async Task<EnqueueResult> EnqueueAsync(JobKind kind, object parameters, JobTrigger trigger, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var record = new JobRecord
        {
            Id = Guid.CreateVersion7(now),
            Kind = kind,
            State = JobState.Queued,
            Trigger = trigger,
            ParametersJson = JsonSerializer.Serialize(parameters, parameters.GetType(), JobJson.Options),
            QueuedAt = now,
        };

        db.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new EnqueueResult(true, record.ToStatus());
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && pg.ConstraintName == JobRecordConfiguration.OneActivePerKindIndex)
        {
            db.Entry(record).State = EntityState.Detached;
            var active = await db.Set<JobRecord>().AsNoTracking()
                .Where(j => j.Kind == kind && (j.State == JobState.Queued || j.State == JobState.Running))
                .FirstOrDefaultAsync(cancellationToken);

            // The active job may have finished in between: then the queue has room again.
            return active is null
                ? await EnqueueAsync(kind, parameters, trigger, cancellationToken)
                : new EnqueueResult(false, active.ToStatus());
        }
    }

    public async Task<JobStatus?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Set<JobRecord>().AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, cancellationToken))?.ToStatus();

    public async Task<IReadOnlyList<JobStatus>> ListAsync(JobKind? kind, int limit, CancellationToken cancellationToken)
    {
        var query = db.Set<JobRecord>().AsNoTracking();
        if (kind is { } k)
        {
            query = query.Where(j => j.Kind == k);
        }

        var records = await query.OrderByDescending(j => j.QueuedAt).Take(limit).ToListAsync(cancellationToken);
        return [.. records.Select(r => r.ToStatus())];
    }

    public Task<bool> AnyQueuedSinceAsync(JobKind kind, JobTrigger trigger, DateTimeOffset since, CancellationToken cancellationToken) =>
        db.Set<JobRecord>().AnyAsync(j => j.Kind == kind && j.Trigger == trigger && j.QueuedAt >= since, cancellationToken);

    public async Task<JobStatus?> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await db.Set<JobRecord>()
            .Where(j => j.Id == id && j.State == JobState.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, JobState.Cancelled).SetProperty(j => j.FinishedAt, now), cancellationToken);
        await db.Set<JobRecord>()
            .Where(j => j.Id == id && j.State == JobState.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.CancelRequested, true), cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Takes the oldest queued job, or a running job of a lost worker, and marks it running under a new lease.
    /// A lost job that used up its attempts is failed instead. Returns null when nothing is due.
    /// </summary>
    public async Task<ClaimedJob?> ClaimNextAsync(string workerId, CancellationToken cancellationToken)
    {
        // A lost job that used up its attempts, or a job cancelled before a worker got it, is closed; then look again.
        while (true)
        {
            var (claimed, closedOne) = await db.Database.CreateExecutionStrategy()
                .ExecuteAsync(ct => ClaimOnceAsync(workerId, ct), cancellationToken);
            if (claimed is not null || !closedOne)
            {
                return claimed;
            }
        }
    }

    private async Task<(ClaimedJob? Claimed, bool ClosedOne)> ClaimOnceAsync(string workerId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var staleBefore = now.AddSeconds(-settings.LeaseSeconds);

        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Not composed with LINQ: EF sends the statement as written, row lock included.
        var candidates = await db.Set<JobRecord>()
            .FromSql($"""
                SELECT * FROM ingestion.jobs
                WHERE state = 'Queued' OR (state = 'Running' AND heartbeat_at < {staleBefore})
                ORDER BY queued_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
        if (candidates is not [var job])
        {
            return (null, false);
        }

        if (job.CancelRequested || (job.State == JobState.Running && job.Attempts >= settings.MaxAttempts))
        {
            job.State = job.CancelRequested ? JobState.Cancelled : JobState.Failed;
            job.Error = job.CancelRequested ? null : $"The worker running the job stopped sending heartbeats ({job.Attempts} attempts).";
            job.FinishedAt = now;
            job.LeaseId = null;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (null, true);
        }

        var lease = Guid.NewGuid();
        job.State = JobState.Running;
        job.Attempts++;
        job.WorkerId = workerId;
        job.LeaseId = lease;
        job.StartedAt ??= now;
        job.HeartbeatAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (new ClaimedJob(job.ToStatus(), lease), false);
    }

    /// <summary>Refreshes the heartbeat and progress while the worker holds the lease.</summary>
    public async Task<HeartbeatOutcome> HeartbeatAsync(Guid jobId, Guid lease, string? progressJson, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var updated = await Owned(jobId, lease)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.HeartbeatAt, now).SetProperty(j => j.ProgressJson, j => progressJson ?? j.ProgressJson),
                cancellationToken);
        if (updated == 0)
        {
            return HeartbeatOutcome.LeaseLost;
        }

        var cancel = await db.Set<JobRecord>().Where(j => j.Id == jobId).Select(j => j.CancelRequested).FirstAsync(cancellationToken);
        return cancel ? HeartbeatOutcome.CancelRequested : HeartbeatOutcome.Continue;
    }

    /// <summary>Writes the outcome. False when the lease was lost meanwhile (another worker owns the job now).</summary>
    public async Task<bool> FinishAsync(
        Guid jobId,
        Guid lease,
        JobState state,
        string? progressJson,
        string? resultJson,
        string? error,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        error = error is { Length: > MaxErrorLength } ? error[..MaxErrorLength] : error;
        var updated = await Owned(jobId, lease)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(j => j.State, state)
                    .SetProperty(j => j.ProgressJson, j => progressJson ?? j.ProgressJson)
                    .SetProperty(j => j.ResultJson, resultJson)
                    .SetProperty(j => j.Error, error)
                    .SetProperty(j => j.FinishedAt, now)
                    .SetProperty(j => j.HeartbeatAt, now)
                    .SetProperty(j => j.LeaseId, (Guid?)null),
                cancellationToken);
        return updated > 0;
    }

    /// <summary>Puts a job back in the queue when its worker shuts down; the stop does not count as an attempt.</summary>
    public Task ReleaseAsync(Guid jobId, Guid lease, CancellationToken cancellationToken) =>
        Owned(jobId, lease)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(j => j.State, JobState.Queued)
                    .SetProperty(j => j.Attempts, j => j.Attempts - 1)
                    .SetProperty(j => j.WorkerId, (string?)null)
                    .SetProperty(j => j.LeaseId, (Guid?)null),
                cancellationToken);

    private IQueryable<JobRecord> Owned(Guid jobId, Guid lease) =>
        db.Set<JobRecord>().Where(j => j.Id == jobId && j.LeaseId == lease && j.State == JobState.Running);
}
