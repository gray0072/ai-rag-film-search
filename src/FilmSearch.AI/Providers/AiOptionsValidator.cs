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

        foreach (var (profileName, profile) in options.ChatProfiles)
        {
            var path = $"AI:ChatProfiles:{profileName}";
            ValidateConnection(errors, path, profile, isActive: Same(profileName, options.ActiveChatProfile));

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

        if (isActive && string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            errors.Add($"{path}:ApiKey is required for the active profile. Set it with "
                + $"`dotnet user-secrets set {path}:ApiKey <key> --project src/FilmSearch.Api`.");
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Names(IEnumerable<string> names) => string.Join(", ", names);
}
