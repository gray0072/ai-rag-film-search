namespace FilmSearch.Core.Search;

// Contracts of the embedding index (spec 08 §4): reindex, statistics, deleting the vectors of an old model,
// and the background job that runs a reindex. Used by the admin endpoints (and by the Worker from I16).
// Doc comments of types in the OpenAPI document stay on one line (see SearchModels.cs).

/// <summary>What to (re)embed. <c>Profile</c>: embedding profile, the active one when null. <c>OnlyStale</c>: embed only movies whose text hash changed; false re-embeds everything. <c>Locales</c>: locales to keep vectors for, the configured ones when null; vectors of other locales of this model are deleted.</summary>
public sealed record ReindexRequest(string? Profile = null, bool OnlyStale = true, IReadOnlyList<string>? Locales = null);

/// <summary>Progress of a reindex: <c>Processed</c> of <c>Total</c> movie texts (movies × locales) checked so far.</summary>
public sealed record ReindexProgress(int Total, int Processed, int Embedded, int Unchanged);

/// <summary>Outcome of a reindex. <c>Deleted</c>: vectors of this model for locales that are no longer indexed.</summary>
public sealed record ReindexResult(
    string Profile,
    string Model,
    int Dimensions,
    IReadOnlyList<string> Locales,
    int Movies,
    int Embedded,
    int Unchanged,
    int Deleted,
    long ElapsedMilliseconds);

/// <summary>Number of stored vectors of one model and locale.</summary>
public sealed record EmbeddingSetStats(string Model, int Dimensions, string Locale, int Count);

/// <summary>Keeps movie embeddings in sync with the catalog (FR-DATA-3, FR-DATA-4).</summary>
public interface IMovieEmbeddingIndexer
{
    /// <summary>
    /// Builds the embedding text of every movie and locale, compares its hash with the stored vector and embeds
    /// the stale ones in batches. Vectors are stored under the profile's model, so another model's vectors stay untouched.
    /// </summary>
    /// <exception cref="ArgumentException">Unknown profile or locale.</exception>
    /// <exception cref="AiProviderUnavailableException">The embedding provider failed; batches saved before stay saved.</exception>
    Task<ReindexResult> ReindexAsync(ReindexRequest request, IProgress<ReindexProgress>? progress, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmbeddingSetStats>> GetStatsAsync(CancellationToken cancellationToken);

    /// <summary>Deletes all vectors of a model. Returns the number of deleted vectors.</summary>
    /// <exception cref="InvalidOperationException">The model is the active profile's model: search needs it.</exception>
    Task<int> DeleteModelAsync(string model, CancellationToken cancellationToken);
}

public enum EmbeddingJobState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>State of a background reindex job. <c>Progress</c> while it runs, <c>Result</c> when it succeeded, <c>Error</c> when it failed.</summary>
public sealed record EmbeddingJobStatus
{
    public required Guid Id { get; init; }

    public required EmbeddingJobState State { get; init; }

    public required ReindexRequest Request { get; init; }

    public required DateTimeOffset QueuedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public ReindexProgress? Progress { get; init; }

    public ReindexResult? Result { get; init; }

    public string? Error { get; init; }

    public bool IsActive => State is EmbeddingJobState.Queued or EmbeddingJobState.Running;
}

/// <summary>Runs reindex jobs in the background, one at a time (spec 04: the reindex endpoint starts a job).</summary>
public interface IEmbeddingJobs
{
    /// <summary>The running job, or the last finished one; null when no job ran since start.</summary>
    EmbeddingJobStatus? Current { get; }

    /// <summary>Queues a job. Returns false, with the active job's status, when a job is already queued or running.</summary>
    bool TryStart(ReindexRequest request, out EmbeddingJobStatus status);
}
