using System.Diagnostics;
using System.Net;
using FilmSearch.Core.Ingestion;
using FilmSearch.Infrastructure.Tmdb;
using FilmSearch.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.UnitTests.Tmdb;

/// <summary>The TMDB client with its real HTTP pipeline (auth, retry, rate limiter) over recorded responses (spec 08 §3).</summary>
public sealed class TmdbClientTests
{
    public const string V3Key = "0123456789abcdef0123456789abcdef";
    public const string V4Token = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJ0ZXN0In0.c2lnbmF0dXJlLW9mLWEtdGVzdC10b2tlbg";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_recorded_details_genres_and_list_pages()
    {
        var stub = new TmdbStub();
        using var services = Create(stub);
        var tmdb = services.GetRequiredService<TmdbClient>();

        var movie = (await tmdb.GetMovieAsync(278, Ct)).ShouldNotBeNull();
        var genres = await tmdb.GetGenresAsync("ru", Ct);
        var page = await tmdb.GetListPageAsync(TmdbMovieList.TopRated, 1, Ct);

        movie.Title.ShouldBe("The Shawshank Redemption");
        movie.Credits!.Cast.ShouldNotBeNull().ShouldNotBeEmpty();
        movie.Translations!.Translations.ShouldNotBeNull().ShouldContain(t => t.Iso6391 == "ru");
        genres.Genres.ShouldContain(g => g.Id == 18);
        page.Results.Select(r => r.Id).ShouldBe([278, 550, 680, 999_999_001]);

        stub.Requests.Select(r => r.Path).ShouldBe(["/3/movie/278", "/3/genre/movie/list", "/3/movie/top_rated"]);
        stub.Requests[0].Query.ShouldContain("append_to_response=credits,keywords,translations,external_ids");
        stub.Requests[1].Query.ShouldContain("language=ru-RU");
    }

    [Fact]
    public async Task A_v3_key_goes_into_the_query_and_a_v4_token_into_the_bearer_header()
    {
        var v3 = new TmdbStub();
        var v4 = new TmdbStub();
        using (var services = Create(v3))
        {
            await services.GetRequiredService<TmdbClient>().GetMovieAsync(278, Ct);
        }

        using (var services = Create(v4, new() { ["Tmdb:ApiKey"] = V4Token }))
        {
            await services.GetRequiredService<TmdbClient>().GetMovieAsync(278, Ct);
        }

        v3.Requests[0].Query.ShouldEndWith($"&api_key={V3Key}");
        v3.Requests[0].Authorization.ShouldBeNull();
        v4.Requests[0].Query.ShouldNotContain("api_key");
        v4.Requests[0].Authorization.ShouldBe($"Bearer {V4Token}");
    }

    [Fact]
    public async Task Retries_429_after_the_Retry_After_delay_and_5xx_with_backoff()
    {
        var stub = new TmdbStub()
            .Script("/3/movie/278", () => TmdbStub.Status(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(1)))
            .Script("/3/movie/550", () => TmdbStub.Status(HttpStatusCode.BadGateway), () => TmdbStub.Status(HttpStatusCode.ServiceUnavailable));
        using var services = Create(stub);
        var tmdb = services.GetRequiredService<TmdbClient>();

        var stopwatch = Stopwatch.StartNew();
        (await tmdb.GetMovieAsync(278, Ct)).ShouldNotBeNull();
        var afterRetryAfter = stopwatch.Elapsed;
        (await tmdb.GetMovieAsync(550, Ct)).ShouldNotBeNull();

        stub.Count("/3/movie/278").ShouldBe(2);
        stub.Count("/3/movie/550").ShouldBe(3);
        afterRetryAfter.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900), "the Retry-After header wins over the 10 ms backoff");
    }

    [Fact]
    public async Task An_unknown_movie_is_null_and_an_auth_error_fails_at_once_without_the_key_in_the_message()
    {
        var stub = new TmdbStub().Script("/3/movie/550", () => TmdbStub.Status(HttpStatusCode.Unauthorized));
        using var services = Create(stub);
        var tmdb = services.GetRequiredService<TmdbClient>();

        (await tmdb.GetMovieAsync(1, Ct)).ShouldBeNull();
        var error = await Should.ThrowAsync<HttpRequestException>(() => tmdb.GetMovieAsync(550, Ct));

        stub.Count("/3/movie/1").ShouldBe(1);
        stub.Count("/3/movie/550").ShouldBe(1);
        error.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        error.Message.ShouldContain("movie/550");
        error.Message.ShouldNotContain(V3Key);
    }

    [Fact]
    public async Task Requests_over_the_rate_limit_wait_for_the_next_window()
    {
        var stub = new TmdbStub();
        using var services = Create(stub, new() { ["Tmdb:RequestsPerWindow"] = "2", ["Tmdb:WindowSeconds"] = "1" });
        var tmdb = services.GetRequiredService<TmdbClient>();

        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => tmdb.GetGenresAsync("en", Ct)));

        // 2 + 2 + 1 permits: the fifth request starts in the third window.
        stub.Requests.Count.ShouldBe(5);
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1800));
    }

    [Fact]
    public async Task Without_a_key_no_request_is_sent()
    {
        var stub = new TmdbStub();
        using var services = Create(stub, new() { ["Tmdb:ApiKey"] = "" });

        var error = await Should.ThrowAsync<InvalidOperationException>(() => services.GetRequiredService<TmdbClient>().GetMovieAsync(278, Ct));

        error.Message.ShouldContain("Tmdb:ApiKey");
        stub.Requests.ShouldBeEmpty();
    }

    /// <summary>The client as the Worker registers it, with <paramref name="stub"/> as the network.</summary>
    internal static ServiceProvider Create(TmdbStub stub, Dictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Tmdb:ApiKey"] = V3Key,
            ["Tmdb:RetryBaseDelayMs"] = "10",
        };
        foreach (var (key, value) in settings ?? [])
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTmdbClient(configuration);
        services.AddHttpClient(TmdbClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => stub);
        return services.BuildServiceProvider();
    }
}
