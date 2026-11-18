using System.Text.Json;

namespace FilmSearch.Core.Ingestion;

// Background jobs (I16, spec 08 §3): a job table in PostgreSQL is the queue between the API (enqueues) and the Worker
// (claims with FOR UPDATE SKIP LOCKED and runs). Doc comments of types in the OpenAPI document stay on one line.

/// <summary>What a background job does.</summary>
public enum JobKind
{
    TmdbImport,
    EmbeddingReindex,
}

/// <summary>Lifecycle of a job: Queued → Running → Succeeded / Failed / Cancelled. A running job whose worker stopped sending heartbeats is queued again.</summary>
public enum JobState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>Who queued a job: an admin endpoint, the daily schedule of the Worker, or the API on startup (Development).</summary>
public enum JobTrigger
{
    Admin,
    Schedule,
    Startup,
}

/// <summary>A background job: parameters, progress while it runs, result when it succeeded, error when it failed. Parameters, progress and result are JSON whose shape depends on the kind.</summary>
public sealed record JobStatus
{
    public required Guid Id { get; init; }

    public required JobKind Kind { get; init; }

    public required JobState State { get; init; }

    public required JobTrigger Trigger { get; init; }

    public required JsonElement Parameters { get; init; }

    public JsonElement? Progress { get; init; }

    public JsonElement? Result { get; init; }

    public string? Error { get; init; }

    /// <summary>Number of times a worker started the job (more than one after a worker was lost).</summary>
    public int Attempts { get; init; }

    public bool CancelRequested { get; init; }

    /// <summary>The worker process that runs or ran the job.</summary>
    public string? WorkerId { get; init; }

    public required DateTimeOffset QueuedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? HeartbeatAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public bool IsActive => State is JobState.Queued or JobState.Running;

    /// <summary>The parameters as <typeparamref name="T"/>.</summary>
    public T GetParameters<T>() => Parameters.Deserialize<T>(JobJson.Options)
        ?? throw new InvalidOperationException($"Job {Id} has no parameters.");

    public T? GetProgress<T>() => Progress is { } progress ? progress.Deserialize<T>(JobJson.Options) : default;

    public T? GetResult<T>() => Result is { } result ? result.Deserialize<T>(JobJson.Options) : default;
}

/// <summary>Outcome of <see cref="IBackgroundJobs.EnqueueAsync"/>: the new job, or the active job of the same kind when one is queued or running.</summary>
public sealed record EnqueueResult(bool Created, JobStatus Job);

/// <summary>The job table as a queue between the API and the Worker (FR-DATA-2). One active (queued or running) job per kind.</summary>
public interface IBackgroundJobs
{
    /// <summary>Queues a job. When a job of the same kind is queued or running, nothing is queued and that job is returned.</summary>
    Task<EnqueueResult> EnqueueAsync(JobKind kind, object parameters, JobTrigger trigger, CancellationToken cancellationToken);

    Task<JobStatus?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<JobStatus>> ListAsync(JobKind? kind, int limit, CancellationToken cancellationToken);

    /// <summary>Whether a job of this kind and trigger was queued at or after <paramref name="since"/> (the daily schedule).</summary>
    Task<bool> AnyQueuedSinceAsync(JobKind kind, JobTrigger trigger, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>A queued job is cancelled at once; a running one at its next heartbeat. Returns null for an unknown id; a finished job is returned unchanged.</summary>
    Task<JobStatus?> CancelAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>JSON of job parameters, progress and results in the database: camelCase, enums as camelCase strings (like the API).</summary>
public static class JobJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
