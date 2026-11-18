using System.ComponentModel.DataAnnotations;
using FilmSearch.Core.Ingestion;
using FilmSearch.Core.Localization;
using FilmSearch.Core.Search;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FilmSearch.Api.Modules.Admin;

/// <summary>
/// Admin endpoints of the embedding index (spec 04 "Admin", spec 08 §4): state, background reindex, deleting the vectors
/// of an old model. Switching models: reindex with the new profile, switch <c>AI:ActiveEmbeddingProfile</c>, then delete the old model.
/// </summary>
internal static class AdminEmbeddingEndpoints
{
    public static RouteGroupBuilder MapAdminEmbeddingEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/embeddings", GetState)
            .WithName("GetEmbeddingIndex")
            .WithSummary("Active embedding model, stored vectors per model and locale, and the current or last reindex job.");

        group.MapPost("/embeddings/reindex", Reindex)
            .WithName("ReindexEmbeddings")
            .WithSummary("Queues a reindex job for the Worker (202). 409 while another reindex job is queued or running.");

        group.MapDelete("/embeddings", DeleteModel)
            .WithName("DeleteEmbeddingModel")
            .WithSummary("Deletes all vectors of a model that is not the active one.");

        return group;
    }

    private static async Task<Ok<EmbeddingIndexState>> GetState(
        ITextEmbedder embedder,
        IMovieEmbeddingIndexer indexer,
        IEmbeddingJobs jobs,
        CancellationToken cancellationToken)
    {
        var active = embedder.Active;
        var stats = await indexer.GetStatsAsync(cancellationToken);
        return TypedResults.Ok(new EmbeddingIndexState(active.Profile, active.Model, active.Dimensions, stats, await jobs.GetLatestAsync(cancellationToken)));
    }

    private static async Task<Results<Accepted<EmbeddingJobStatus>, Conflict<EmbeddingJobStatus>, ValidationProblem>> Reindex(
        AdminReindexRequest request,
        ITextEmbedder embedder,
        IEmbeddingJobs jobs,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var profile = string.IsNullOrWhiteSpace(request.Profile) ? null : request.Profile.Trim();
        if (profile is not null)
        {
            try
            {
                profile = embedder.GetModel(profile).Profile;
            }
            catch (ArgumentException ex)
            {
                errors["profile"] = [ex.Message];
            }
        }

        var unknown = request.Locales?.Where(l => !Locales.All.Contains(l.Trim().ToLowerInvariant())).ToList();
        if (unknown is { Count: > 0 })
        {
            errors["locales"] = [$"Unknown locale(s): {string.Join(", ", unknown)}. Supported: {string.Join(", ", Locales.All)}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var locales = request.Locales is { Count: > 0 } ? [.. request.Locales.Select(l => l.Trim().ToLowerInvariant()).Distinct()] : (IReadOnlyList<string>?)null;
        var (started, status) = await jobs.TryStartAsync(new ReindexRequest(profile, request.OnlyStale, locales), JobTrigger.Admin, cancellationToken);
        return started
            ? TypedResults.Accepted($"/api/admin/jobs/{status.Id}", status)
            : TypedResults.Conflict(status);
    }

    private static async Task<Results<Ok<DeleteEmbeddingsResponse>, ValidationProblem>> DeleteModel(
        [Required][StringLength(200, MinimumLength = 1)] string model,
        IMovieEmbeddingIndexer indexer,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(new DeleteEmbeddingsResponse(model, await indexer.DeleteModelAsync(model, cancellationToken)));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["model"] = [ex.Message] });
        }
    }
}

/// <summary>Body of <c>POST /api/admin/embeddings/reindex</c>.</summary>
public sealed class AdminReindexRequest
{
    /// <summary>Embedding profile from <c>AI:EmbeddingProfiles</c>; the active one when empty.</summary>
    [StringLength(100)]
    public string? Profile { get; init; }

    /// <summary>Embed only movies whose embedding text changed (default). False re-embeds every movie.</summary>
    public bool OnlyStale { get; init; } = true;

    /// <summary>Locales to keep vectors for (<c>en</c>, <c>ru</c>, <c>sv</c>); <c>Search:Embeddings:Locales</c> when empty. Vectors of other locales of this model are deleted.</summary>
    [MaxLength(3)]
    public IReadOnlyList<string>? Locales { get; init; }
}

/// <summary>State of the embedding index: the active model and the stored vectors of every model.</summary>
public sealed record EmbeddingIndexState(
    string ActiveProfile,
    string ActiveModel,
    int ActiveDimensions,
    IReadOnlyList<EmbeddingSetStats> Sets,
    EmbeddingJobStatus? Job);

public sealed record DeleteEmbeddingsResponse(string Model, int Deleted);
