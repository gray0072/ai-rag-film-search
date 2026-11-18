using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using FilmSearch.Core.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace FilmSearch.Infrastructure.Tmdb;

/// <summary>Configuration section <c>Tmdb</c> (spec 08 §3). The key lives in user secrets or the environment, never in files.</summary>
public sealed class TmdbOptions
{
    public const string SectionName = "Tmdb";

    /// <summary>A v4 read access token (sent as a bearer token) or a v3 API key (sent as <c>api_key</c>).</summary>
    public string? ApiKey { get; set; }

    public Uri BaseUrl { get; set; } = new("https://api.themoviedb.org/3/");

    /// <summary>Requests per <see cref="WindowSeconds"/>: about 40 per 10 seconds (spec 08 §3). Requests over the limit wait.</summary>
    public int RequestsPerWindow { get; set; } = 40;

    public int WindowSeconds { get; set; } = 10;

    /// <summary>Movie detail requests in flight at once.</summary>
    public int MaxConcurrency { get; set; } = 4;

    public int MaxRetries { get; set; } = 5;

    /// <summary>First retry delay (exponential with jitter). A <c>Retry-After</c> header of a 429 wins.</summary>
    public int RetryBaseDelayMs { get; set; } = 1000;

    public int AttemptTimeoutSeconds { get; set; } = 20;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>A v4 read access token is a JWT; a v3 API key is 32 hex characters.</summary>
    internal bool IsBearerToken => ApiKey is { Length: > 40 } key && key.Contains('.', StringComparison.Ordinal);
}

/// <summary>
/// Typed TMDB v3 client (spec 08 §3). Resilience and the rate limit are in the HTTP pipeline (<see cref="TmdbServiceCollectionExtensions.AddTmdbClient"/>):
/// retries of 429 / 5xx / timeouts honoring <c>Retry-After</c>, and a sliding window limiter shared by every request of the process.
/// </summary>
public sealed class TmdbClient(HttpClient http)
{
    public const string HttpClientName = "tmdb";

    /// <summary>The <c>language</c> parameter of a locale.</summary>
    public static string Language(string locale) => locale switch
    {
        "ru" => "ru-RU",
        "sv" => "sv-SE",
        _ => "en-US",
    };

    public Task<TmdbGenreList> GetGenresAsync(string locale, CancellationToken cancellationToken) =>
        GetAsync<TmdbGenreList>($"genre/movie/list?language={Language(locale)}", cancellationToken)!;

    public Task<TmdbPage<TmdbListItem>> GetListPageAsync(TmdbMovieList list, int page, CancellationToken cancellationToken)
    {
        var path = list switch
        {
            TmdbMovieList.TopRated => "movie/top_rated",
            TmdbMovieList.Popular => "movie/popular",
            _ => throw new ArgumentOutOfRangeException(nameof(list), list, null),
        };
        return GetAsync<TmdbPage<TmdbListItem>>(
            string.Create(CultureInfo.InvariantCulture, $"{path}?language=en-US&page={page}"),
            cancellationToken)!;
    }

    /// <summary>Details in English with credits, keywords, translations and external ids (what <see cref="TmdbMovieMapper"/> needs). Null when TMDB does not know the id.</summary>
    public async Task<TmdbMovieDetails?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken)
    {
        try
        {
            return await GetAsync<TmdbMovieDetails>(
                string.Create(CultureInfo.InvariantCulture, $"movie/{tmdbId}?language=en-US&append_to_response=credits,keywords,translations,external_ids"),
                cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The path has no key: the auth handler adds it to the request only.
            throw new HttpRequestException(
                $"TMDB {path} returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                inner: null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<T>(TmdbJson.Options, cancellationToken)
            ?? throw new HttpRequestException($"TMDB {path} returned an empty body.");
    }
}

/// <summary>Adds the key: a bearer header for a v4 token, the <c>api_key</c> query parameter for a v3 key.</summary>
internal sealed class TmdbAuthHandler(IOptions<TmdbOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException(TmdbServiceCollectionExtensions.MissingKeyMessage);
        }

        if (settings.IsBearerToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }
        else if (request.RequestUri is { } uri)
        {
            var separator = uri.Query.Length > 0 ? "&" : "?";
            request.RequestUri = new Uri(uri + separator + "api_key=" + Uri.EscapeDataString(settings.ApiKey!));
        }

        return base.SendAsync(request, cancellationToken);
    }
}

// RemoveAllResilienceHandlers is marked experimental; it is the supported way to opt a client out of
// ConfigureHttpClientDefaults (ServiceDefaults adds the standard resilience handler to every client).
#pragma warning disable EXTEXP0001

public static class TmdbServiceCollectionExtensions
{
    public const string MissingKeyMessage =
        "TMDB API key is missing: set Tmdb:ApiKey (a v4 read access token or a v3 key) in the user secrets of the API "
        + "(the Worker shares them): dotnet user-secrets set Tmdb:ApiKey <key> --project src/FilmSearch.Api";

    /// <summary>
    /// <see cref="TmdbClient"/> with its own resilience pipeline instead of the standard one, outer to inner:
    /// total timeout → retry (429, 5xx, 408, network errors, attempt timeouts; <c>Retry-After</c> wins) → rate limiter
    /// (each attempt takes a permit; requests over the limit queue) → attempt timeout.
    /// </summary>
    public static IServiceCollection AddTmdbClient(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.AddOptions<TmdbOptions>()
            .Bind(configuration.GetSection(TmdbOptions.SectionName))
            .Validate(o => o.RequestsPerWindow is >= 1 and <= 1000 && o.WindowSeconds is >= 1 and <= 600, "Tmdb:RequestsPerWindow (1–1000) and Tmdb:WindowSeconds (1–600) are out of range.")
            .Validate(o => o.MaxConcurrency is >= 1 and <= 32, "Tmdb:MaxConcurrency must be between 1 and 32.")
            .Validate(o => o.MaxRetries is >= 0 and <= 10, "Tmdb:MaxRetries must be between 0 and 10.")
            .ValidateOnStart();

        services.AddTransient<TmdbAuthHandler>();
        services.AddHttpClient<TmdbClient>(TmdbClient.HttpClientName, (sp, client) =>
            {
                client.BaseAddress = sp.GetRequiredService<IOptions<TmdbOptions>>().Value.BaseUrl;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<TmdbAuthHandler>()
            .RemoveAllResilienceHandlers()
            .AddResilienceHandler("tmdb", (pipeline, context) =>
            {
                var o = context.ServiceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;

                // One limiter per pipeline, and the pipeline is built once per client name: every TMDB call of the process shares it.
                var limiter = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = o.RequestsPerWindow,
                    Window = TimeSpan.FromSeconds(o.WindowSeconds),
                    SegmentsPerWindow = Math.Min(o.WindowSeconds, 10),
                    QueueLimit = 100_000,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                });
                context.OnPipelineDisposed(limiter.Dispose);

                pipeline.AddTimeout(TimeSpan.FromMinutes(5));
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = Math.Max(o.MaxRetries, 1),
                    ShouldHandle = o.MaxRetries == 0
                        ? _ => PredicateResult.False()
                        : args => ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(args.Outcome)),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(o.RetryBaseDelayMs),
                    MaxDelay = TimeSpan.FromMinutes(1),
                    ShouldRetryAfterHeader = true,
                });
                pipeline.AddRateLimiter(limiter);
                pipeline.AddTimeout(TimeSpan.FromSeconds(o.AttemptTimeoutSeconds));
            });

        return services;
    }
}
