using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace FilmSearch.Tests.Fixtures;

/// <summary>
/// A fake TMDB API for tests (compiled into the unit and the integration test projects): answers from the recorded
/// responses in <c>tests/fixtures/tmdb</c>, 404 for unknown movies, and scripted answers per path for retries and errors.
/// Every request is recorded with its authorization header.
/// </summary>
public sealed class TmdbStub : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<HttpResponseMessage>>> _scripted = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();
    private readonly ConcurrentDictionary<string, string> _overrides = new(StringComparer.Ordinal);

    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "fixtures", "tmdb");

    /// <summary>The TMDB ids with a recorded details response.</summary>
    public static IReadOnlyList<int> RecordedMovies { get; } = [278, 550, 680];

    public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

    /// <summary>Requests of one path (without query), e.g. <c>/3/movie/278</c>.</summary>
    public int Count(string path) => _requests.Count(r => r.Path == path);

    /// <summary>The next requests of <paramref name="path"/> get these answers, then the recorded one again.</summary>
    public TmdbStub Script(string path, params Func<HttpResponseMessage>[] answers)
    {
        var queue = _scripted.GetOrAdd(path, _ => new ConcurrentQueue<Func<HttpResponseMessage>>());
        foreach (var answer in answers)
        {
            queue.Enqueue(answer);
        }

        return this;
    }

    /// <summary>Replaces the recorded body of a path (a changed movie).</summary>
    public TmdbStub Override(string path, string json)
    {
        _overrides[path] = json;
        return this;
    }

    public static HttpResponseMessage Status(HttpStatusCode status, TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("""{"status_message":"scripted"}""", Encoding.UTF8, "application/json") };
        if (retryAfter is { } delay)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
        }

        return response;
    }

    public static string Recorded(string name) => File.ReadAllText(Path.Combine(Directory, name + ".json"));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;
        _requests.Enqueue(new RecordedRequest(path, uri.Query, request.Headers.Authorization?.ToString()));

        if (_scripted.TryGetValue(path, out var queue) && queue.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted());
        }

        if (_overrides.TryGetValue(path, out var body))
        {
            return Task.FromResult(Json(body));
        }

        var file = FileFor(path, uri.Query);
        return Task.FromResult(file is not null && File.Exists(file)
            ? Json(File.ReadAllText(file))
            : Status(HttpStatusCode.NotFound));
    }

    private static string? FileFor(string path, string query)
    {
        var relative = path.StartsWith("/3/", StringComparison.Ordinal) ? path[3..] : path.TrimStart('/');
        if (relative == "genre/movie/list")
        {
            var locale = query.Contains("language=ru", StringComparison.Ordinal) ? "ru" : query.Contains("language=sv", StringComparison.Ordinal) ? "sv" : "en";
            return Path.Combine(Directory, $"genres-{locale}.json");
        }

        if (relative is "movie/top_rated" or "movie/popular")
        {
            // One recorded page (total_pages = 1) serves both lists.
            return query.Contains("page=1&", StringComparison.Ordinal) || query.EndsWith("page=1", StringComparison.Ordinal)
                ? Path.Combine(Directory, "top_rated-p1.json")
                : null;
        }

        return relative.StartsWith("movie/", StringComparison.Ordinal) ? Path.Combine(Directory, relative.Replace('/', '-') + ".json") : null;
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public sealed record RecordedRequest(string Path, string Query, string? Authorization);
}
