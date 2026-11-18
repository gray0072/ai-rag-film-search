using System.ComponentModel.DataAnnotations;
using FilmSearch.Core.Ingestion;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FilmSearch.Api.Modules.Admin;

/// <summary>
/// Admin endpoints of background jobs (I16, spec 04 "Admin", spec 08 §3): queue a TMDB import, list and inspect jobs of
/// every kind, cancel one. The API only writes rows of the job table; the Worker runs them.
/// </summary>
internal static class AdminJobEndpoints
{
    public const int MaxPages = 500;
    public const int MaxTmdbIds = 5000;
    public const int DefaultListLimit = 20;
    public const int MaxListLimit = 100;

    public static RouteGroupBuilder MapAdminJobEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/ingestion/tmdb", ImportFromTmdb)
            .WithName("ImportFromTmdb")
            .WithSummary("Queues a TMDB import job for the Worker (202): a list with pages, or TMDB ids. 409 while another import is queued or running.");

        group.MapGet("/jobs", ListJobs)
            .WithName("ListJobs")
            .WithSummary("Background jobs, newest first, optionally of one kind (tmdbImport, embeddingReindex).");

        group.MapGet("/jobs/{id:guid}", GetJob)
            .WithName("GetJob")
            .WithSummary("State, progress and result of a background job.");

        group.MapPost("/jobs/{id:guid}/cancel", CancelJob)
            .WithName("CancelJob")
            .WithSummary("Cancels a job: a queued one at once, a running one at its next heartbeat. A finished job is returned unchanged.");

        return group;
    }

    private static async Task<Results<Accepted<JobStatus>, Conflict<JobStatus>, ValidationProblem>> ImportFromTmdb(
        AdminTmdbImportRequest request,
        IBackgroundJobs queue,
        CancellationToken cancellationToken)
    {
        var ids = request.TmdbIds ?? [];
        var errors = new Dictionary<string, string[]>();
        if ((ids.Count > 0) == (request.List is not null))
        {
            errors["list"] = ["Give either a list (topRated, popular) with pages, or tmdbIds."];
        }

        if (ids.Any(id => id <= 0))
        {
            errors["tmdbIds"] = ["TMDB ids must be positive."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var parameters = request.List is { } list
            ? new TmdbImportRequest(list, request.Pages ?? 1)
            : new TmdbImportRequest(TmdbIds: [.. ids.Distinct()]);
        var (created, job) = await queue.EnqueueAsync(JobKind.TmdbImport, parameters, JobTrigger.Admin, cancellationToken);
        return created
            ? TypedResults.Accepted($"/api/admin/jobs/{job.Id}", job)
            : TypedResults.Conflict(job);
    }

    private static async Task<Results<Ok<IReadOnlyList<JobStatus>>, ValidationProblem>> ListJobs(
        IBackgroundJobs queue,
        CancellationToken cancellationToken,
        string? kind = null,
        [Range(1, MaxListLimit)] int? limit = null)
    {
        JobKind? parsed = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<JobKind>(kind.Trim(), ignoreCase: true, out var value) || !Enum.IsDefined(value))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["kind"] = [$"Unknown job kind '{kind}'. Known: tmdbImport, embeddingReindex."],
                });
            }

            parsed = value;
        }

        return TypedResults.Ok(await queue.ListAsync(parsed, limit ?? DefaultListLimit, cancellationToken));
    }

    private static async Task<Results<Ok<JobStatus>, NotFound>> GetJob(Guid id, IBackgroundJobs queue, CancellationToken cancellationToken) =>
        await queue.GetAsync(id, cancellationToken) is { } job ? TypedResults.Ok(job) : TypedResults.NotFound();

    private static async Task<Results<Ok<JobStatus>, NotFound>> CancelJob(Guid id, IBackgroundJobs queue, CancellationToken cancellationToken) =>
        await queue.CancelAsync(id, cancellationToken) is { } job ? TypedResults.Ok(job) : TypedResults.NotFound();
}

/// <summary>Body of <c>POST /api/admin/ingestion/tmdb</c>: a list with pages, or TMDB ids.</summary>
public sealed class AdminTmdbImportRequest
{
    /// <summary>TMDB list to import: <c>topRated</c> or <c>popular</c>.</summary>
    public TmdbMovieList? List { get; init; }

    /// <summary>Pages of the list, 20 movies each (default 1).</summary>
    [Range(1, AdminJobEndpoints.MaxPages)]
    public int? Pages { get; init; }

    /// <summary>TMDB ids to import instead of a list.</summary>
    [MaxLength(AdminJobEndpoints.MaxTmdbIds)]
    public IReadOnlyList<int>? TmdbIds { get; init; }
}
