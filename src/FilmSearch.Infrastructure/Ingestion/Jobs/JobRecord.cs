using System.Text.Json;
using FilmSearch.Core.Ingestion;

namespace FilmSearch.Infrastructure.Ingestion.Jobs;

/// <summary>
/// A row of <c>ingestion.jobs</c>: the queue between the API (enqueues) and the Worker (claims and runs).
/// <see cref="LeaseId"/> is new on every claim; a worker writes heartbeats and the outcome only while it still holds the lease.
/// </summary>
internal sealed class JobRecord
{
    public Guid Id { get; set; }

    public JobKind Kind { get; set; }

    public JobState State { get; set; }

    public JobTrigger Trigger { get; set; }

    public string ParametersJson { get; set; } = "{}";

    public string? ProgressJson { get; set; }

    public string? ResultJson { get; set; }

    public string? Error { get; set; }

    public int Attempts { get; set; }

    public bool CancelRequested { get; set; }

    public string? WorkerId { get; set; }

    public Guid? LeaseId { get; set; }

    public DateTimeOffset QueuedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? HeartbeatAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public JobStatus ToStatus() => new()
    {
        Id = Id,
        Kind = Kind,
        State = State,
        Trigger = Trigger,
        Parameters = Parse(ParametersJson) ?? default,
        Progress = Parse(ProgressJson),
        Result = Parse(ResultJson),
        Error = Error,
        Attempts = Attempts,
        CancelRequested = CancelRequested,
        WorkerId = WorkerId,
        QueuedAt = QueuedAt,
        StartedAt = StartedAt,
        HeartbeatAt = HeartbeatAt,
        FinishedAt = FinishedAt,
    };

    private static JsonElement? Parse(string? json) =>
        json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
}
