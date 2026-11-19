using System.ComponentModel.DataAnnotations;
using FilmSearch.AI.Providers;
using FilmSearch.AI.Telemetry;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace FilmSearch.Api.Modules.Admin;

/// <summary>
/// Admin AI endpoints (I05): one chat call through any configured profile, to check a provider and to see the call
/// as a GenAI trace (model, tokens, cost) in the Aspire dashboard. Not used by the store UI.
/// </summary>
internal static class AdminAiEndpoints
{
    public static RouteGroupBuilder MapAdminAiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/ai/profiles", GetProfiles)
            .WithName("AdminAiProfiles")
            .WithSummary("Configured chat and embedding profiles (name, model, whether an API key is set) and the active ones. Keys and endpoints are not returned.");

        group.MapPost("/ai/chat", Chat)
            .WithName("AdminAiChat")
            .WithSummary("Sends one message to a chat profile (default: the active one) and returns the answer with token usage.");

        return group;
    }

    private static Ok<AdminAiProfiles> GetProfiles(IOptions<AiOptions> options)
    {
        var ai = options.Value;
        return TypedResults.Ok(new AdminAiProfiles(
            ai.ActiveChatProfile,
            ai.FallbackChatProfileName,
            [.. ai.ChatProfiles.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Select(p => new AdminChatProfile(p.Key, p.Value.Model, !string.IsNullOrWhiteSpace(p.Value.ApiKey), p.Value.SupportsTools))],
            ai.ActiveEmbeddingProfile,
            [.. ai.EmbeddingProfiles.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Select(p => new AdminEmbeddingProfile(p.Key, p.Value.Model, p.Value.Dimensions, !string.IsNullOrWhiteSpace(p.Value.ApiKey)))]));
    }

    private static async Task<Results<Ok<AdminAiChatResponse>, ValidationProblem, ProblemHttpResult>> Chat(
        AdminAiChatRequest request,
        IOptions<AiOptions> options,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var profileName = string.IsNullOrWhiteSpace(request.Profile) ? options.Value.ActiveChatProfile : request.Profile.Trim();
        if (!options.Value.ChatProfiles.TryGetValue(profileName, out var profile))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["profile"] = [$"Unknown chat profile '{profileName}'. Configured: {string.Join(", ", options.Value.ChatProfiles.Keys)}."],
            });
        }

        if (string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["profile"] = [$"Chat profile '{profileName}' has no API key (AI:ChatProfiles:{profileName}:ApiKey in user secrets)."],
            });
        }

        var chatClient = services.GetRequiredKeyedService<IChatClient>(profileName);
        try
        {
            var response = await chatClient.GetResponseAsync(request.Message, cancellationToken: cancellationToken);
            var usage = response.AdditionalProperties?.GetValueOrDefault(AiUsage.PropertyKey) as AiUsage;

            return TypedResults.Ok(new AdminAiChatResponse(
                profileName,
                response.ModelId ?? profile.Model,
                response.Text,
                usage?.InputTokens ?? 0,
                usage?.OutputTokens ?? 0,
                usage?.EstimatedCostUsd));
        }
        catch (Exception ex) when (AiProviderErrors.IsProviderFailure(ex, cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "AI provider is not available.",
                detail: $"Chat profile '{profileName}' ({profile.Endpoint}, model {profile.Model}): {ex.Message}");
        }
    }
}

/// <summary>Body of <c>POST /api/admin/ai/chat</c>.</summary>
public sealed class AdminAiChatRequest
{
    /// <summary>The user message.</summary>
    [Required]
    [StringLength(4000, MinimumLength = 1)]
    public string Message { get; init; } = string.Empty;

    /// <summary>Chat profile name from <c>AI:ChatProfiles</c>; the active profile when empty.</summary>
    [StringLength(100)]
    public string? Profile { get; init; }
}

/// <summary>Answer of a chat profile with token usage and estimated cost (null when the model has no price).</summary>
public sealed record AdminAiChatResponse(
    string Profile,
    string Model,
    string Text,
    long InputTokens,
    long OutputTokens,
    decimal? EstimatedCostUsd);

/// <summary>The AI profiles of the configuration (<c>AI:ChatProfiles</c>, <c>AI:EmbeddingProfiles</c>) without endpoints and keys.</summary>
/// <param name="ActiveChatProfile">Profile of the assistant and query understanding.</param>
/// <param name="FallbackChatProfile">Profile that answers when the active chat profile fails; null when none.</param>
/// <param name="ChatProfiles">Chat profiles by name.</param>
/// <param name="ActiveEmbeddingProfile">Profile of the search vectors.</param>
/// <param name="EmbeddingProfiles">Embedding profiles by name.</param>
public sealed record AdminAiProfiles(
    string ActiveChatProfile,
    string? FallbackChatProfile,
    IReadOnlyList<AdminChatProfile> ChatProfiles,
    string ActiveEmbeddingProfile,
    IReadOnlyList<AdminEmbeddingProfile> EmbeddingProfiles);

/// <summary>A chat profile: name, model, whether its API key is set and whether it supports tools.</summary>
public sealed record AdminChatProfile(string Name, string Model, bool HasApiKey, bool SupportsTools);

/// <summary>An embedding profile: name, model, vector dimensions and whether its API key is set.</summary>
public sealed record AdminEmbeddingProfile(string Name, string Model, int Dimensions, bool HasApiKey);
