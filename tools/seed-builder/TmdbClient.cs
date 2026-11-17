using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FilmSearch.Infrastructure.Tmdb;

namespace FilmSearch.SeedBuilder;

/// <summary>
/// Minimal TMDB v3 client for the seed builder: throttled (see <see cref="TmdbOptions.MinRequestInterval"/>),
/// retries 429 (honoring <c>Retry-After</c>) and 5xx, and caches every response body on disk.
/// </summary>
internal sealed class TmdbClient : IDisposable
{
    private const int MaxAttempts = 5;

    private readonly HttpClient _http;
    private readonly TmdbOptions _options;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _concurrency;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    public TmdbClient(TmdbOptions options, string cacheDirectory)
    {
        _options = options;
        _cacheDirectory = cacheDirectory;
        _concurrency = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        _http = new HttpClient { BaseAddress = options.BaseUrl, Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // A v4 read access token is a JWT; a v3 API key is 32 hex characters.
        if (IsBearerToken(options.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        Directory.CreateDirectory(cacheDirectory);
    }

    private int _requests;
    private int _cacheHits;

    public int Requests => Volatile.Read(ref _requests);

    public int CacheHits => Volatile.Read(ref _cacheHits);

    /// <param name="path">Relative path with query, e.g. <c>movie/278?language=en-US</c>.</param>
    /// <param name="cacheKey">File name in the cache; null disables caching.</param>
    /// <param name="cancellationToken">Cancels the request and any retry delay.</param>
    public async Task<T> GetAsync<T>(string path, string? cacheKey, CancellationToken cancellationToken)
    {
        var cacheFile = cacheKey is null ? null : Path.Combine(_cacheDirectory, cacheKey + ".json");
        if (cacheFile is not null && File.Exists(cacheFile))
        {
            Interlocked.Increment(ref _cacheHits);
            await using var cached = File.OpenRead(cacheFile);
            return (await JsonSerializer.DeserializeAsync<T>(cached, TmdbJson.Options, cancellationToken))!;
        }

        var body = await SendAsync(path, cancellationToken);
        if (cacheFile is not null)
        {
            await File.WriteAllTextAsync(cacheFile, body, cancellationToken);
        }

        return JsonSerializer.Deserialize<T>(body, TmdbJson.Options)!;
    }

    public void Dispose()
    {
        _http.Dispose();
        _concurrency.Dispose();
        _gate.Dispose();
    }

    private async Task<string> SendAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsBearerToken(_options.ApiKey))
        {
            path += (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "api_key=" + Uri.EscapeDataString(_options.ApiKey!);
        }

        await _concurrency.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                await ThrottleAsync(cancellationToken);
                Interlocked.Increment(ref _requests);

                HttpResponseMessage response;
                try
                {
                    response = await _http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
                }
                catch (HttpRequestException) when (attempt < MaxAttempts)
                {
                    await Task.Delay(Backoff(attempt), cancellationToken);
                    continue;
                }

                using (response)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsStringAsync(cancellationToken);
                    }

                    var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                    if (!retryable || attempt >= MaxAttempts)
                    {
                        throw new HttpRequestException(
                            $"TMDB {RedactKey(path)} returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                            inner: null,
                            response.StatusCode);
                    }

                    var delay = response.Headers.RetryAfter?.Delta ?? Backoff(attempt);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        TimeSpan wait;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var start = now > _nextRequestAt ? now : _nextRequestAt;
            _nextRequestAt = start + _options.MinRequestInterval;
            wait = start - now;
        }
        finally
        {
            _gate.Release();
        }

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken);
        }
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt));

    private static bool IsBearerToken(string? key) => key is { Length: > 40 } && key.Contains('.', StringComparison.Ordinal);

    private static string RedactKey(string path)
    {
        var index = path.IndexOf("api_key=", StringComparison.Ordinal);
        return index < 0 ? path : path[..index] + "api_key=<redacted>";
    }
}
