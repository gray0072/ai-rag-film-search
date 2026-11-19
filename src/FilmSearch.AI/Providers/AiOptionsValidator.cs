using Microsoft.Extensions.Options;

namespace FilmSearch.AI.Providers;

/// <summary>
/// Checks the <c>AI</c> section on startup, so a typo in a profile fails fast with a clear message instead of failing
/// on the first LLM call. Only the active profiles need an API key: the key of an unused cloud profile may be missing.
/// </summary>
internal sealed class AiOptionsValidator : IValidateOptions<AiOptions>
{
    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        var errors = new List<string>();

        if (!options.ChatProfiles.ContainsKey(options.ActiveChatProfile))
        {
            errors.Add($"AI:ActiveChatProfile '{options.ActiveChatProfile}' is not one of AI:ChatProfiles ({Names(options.ChatProfiles.Keys)}).");
        }

        if (!options.EmbeddingProfiles.ContainsKey(options.ActiveEmbeddingProfile))
        {
            errors.Add($"AI:ActiveEmbeddingProfile '{options.ActiveEmbeddingProfile}' is not one of AI:EmbeddingProfiles ({Names(options.EmbeddingProfiles.Keys)}).");
        }

        if (options.FallbackChatProfileName is { } fallback)
        {
            if (!options.ChatProfiles.TryGetValue(fallback, out var fallbackProfile))
            {
                errors.Add($"AI:FallbackChatProfile '{fallback}' is not one of AI:ChatProfiles ({Names(options.ChatProfiles.Keys)}).");
            }
            else if (Same(fallback, options.ActiveChatProfile))
            {
                errors.Add("AI:FallbackChatProfile must differ from AI:ActiveChatProfile.");
            }
            else if (!fallbackProfile.SupportsTools)
            {
                errors.Add($"AI:FallbackChatProfile '{fallback}' must support tools: the assistant gives it the same tools.");
            }
        }

        if (options.QueryEmbeddingCacheMinutes is < 0 or > 7 * 24 * 60 || options.QueryEmbeddingCacheMaxEntries is < 0 or > 1_000_000)
        {
            errors.Add("AI:QueryEmbeddingCacheMinutes (0–10080) or AI:QueryEmbeddingCacheMaxEntries (0–1000000) is out of range.");
        }

        foreach (var (profileName, profile) in options.ChatProfiles)
        {
            var path = $"AI:ChatProfiles:{profileName}";
            var isUsed = Same(profileName, options.ActiveChatProfile) || Same(profileName, options.FallbackChatProfileName ?? "");
            ValidateConnection(errors, path, profile, isActive: isUsed);

            if (profile.Temperature is < 0 or > 2)
            {
                errors.Add($"{path}:Temperature must be between 0 and 2.");
            }

            if (profile.MaxOutputTokens is <= 0)
            {
                errors.Add($"{path}:MaxOutputTokens must be positive.");
            }
        }

        foreach (var (profileName, profile) in options.EmbeddingProfiles)
        {
            var path = $"AI:EmbeddingProfiles:{profileName}";
            ValidateConnection(errors, path, profile, isActive: Same(profileName, options.ActiveEmbeddingProfile));

            if (profile.Dimensions <= 0)
            {
                errors.Add($"{path}:Dimensions must be positive.");
            }
        }

        foreach (var (model, pricing) in options.Pricing)
        {
            if (pricing.InputPerMTokUsd < 0 || pricing.OutputPerMTokUsd < 0)
            {
                errors.Add($"AI:Pricing:{model} prices must not be negative.");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateConnection(List<string> errors, string path, AiProfileOptions profile, bool isActive)
    {
        if (!Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"{path}:Endpoint must be an absolute http(s) URL.");
        }

        if (string.IsNullOrWhiteSpace(profile.Model))
        {
            errors.Add($"{path}:Model is required.");
        }

        if (profile.TimeoutSeconds <= 0)
        {
            errors.Add($"{path}:TimeoutSeconds must be positive.");
        }

        if (profile.MaxRetries is < 0 or > 10)
        {
            errors.Add($"{path}:MaxRetries must be between 0 and 10.");
        }

        if (profile.RetryDelayMs is < 1 or > 60_000)
        {
            errors.Add($"{path}:RetryDelayMs must be between 1 and 60000.");
        }

        if (profile.CircuitBreakSeconds is < 1 or > 3600)
        {
            errors.Add($"{path}:CircuitBreakSeconds must be between 1 and 3600.");
        }

        if (isActive && string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            errors.Add($"{path}:ApiKey is required for the active and the fallback profile. Set it with "
                + $"`dotnet user-secrets set {path}:ApiKey <key> --project src/FilmSearch.Api`.");
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Names(IEnumerable<string> names) => string.Join(", ", names);
}
