using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FilmSearch.AI.Providers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FilmSearch.AI.Health;

/// <summary>
/// Checks that the provider of the active chat or embedding profile is reachable and lists the configured model
/// (<c>GET {Endpoint}/models</c>). The check is registered with the Degraded failure status: the store without AI
/// keeps working when LM Studio is off, and Aspire does not block the web app on it.
/// </summary>
internal sealed class AiProviderHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<AiOptions> options,
    AiProfileKind kind) : IHealthCheck
{
    internal const string HttpClientName = "FilmSearch.AI.Health";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var ai = options.CurrentValue;
        var (profileName, profile) = kind == AiProfileKind.Chat
            ? (ai.ActiveChatProfile, (AiProfileOptions)ai.GetChatProfile(ai.ActiveChatProfile))
            : (ai.ActiveEmbeddingProfile, ai.GetEmbeddingProfile(ai.ActiveEmbeddingProfile));

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, profile.Endpoint.TrimEnd('/') + "/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new HealthCheckResult(context.Registration.FailureStatus,
                    $"{kind} profile '{profileName}': {profile.Endpoint}/models returned {(int)response.StatusCode}.");
            }

            var models = await response.Content.ReadFromJsonAsync<ModelList>(cancellationToken);
            var listed = models?.Data?.Any(m => string.Equals(m.Id, profile.Model, StringComparison.OrdinalIgnoreCase)) ?? false;

            return listed
                ? HealthCheckResult.Healthy($"{kind} profile '{profileName}': model '{profile.Model}' is available.")
                : new HealthCheckResult(context.Registration.FailureStatus,
                    $"{kind} profile '{profileName}': model '{profile.Model}' is not listed by {profile.Endpoint}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus,
                $"{kind} profile '{profileName}': {profile.Endpoint} is not reachable.", ex);
        }
    }

    private sealed record ModelList([property: JsonPropertyName("data")] List<ModelEntry>? Data);

    private sealed record ModelEntry([property: JsonPropertyName("id")] string Id);
}

internal enum AiProfileKind
{
    Chat,
    Embedding,
}
