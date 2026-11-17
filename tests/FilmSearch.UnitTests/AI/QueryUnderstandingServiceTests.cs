using System.Net;
using System.Text.Json;
using FilmSearch.AI.Prompts;
using FilmSearch.AI.QueryUnderstanding;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FilmSearch.UnitTests.AI;

/// <summary>
/// Query understanding through the real AI pipeline (OpenAI client, Microsoft.Extensions.AI middleware, JSON on the wire)
/// with a fake provider that answers from a script: valid answer, retry after invalid output, fallback.
/// </summary>
public sealed class QueryUnderstandingServiceTests : IDisposable
{
    private const string Valid = """
        {"semanticQuery":"space","filters":{"genresInclude":[],"genresExclude":["horror"],"yearFrom":1990,"yearTo":1999,
         "minRating":7,"maxPriceMinor":null,"language":null},"similarToTitles":[],"detectedLocale":"ru"}
        """;

    private const string Query = "космические фильмы девяностых, рейтинг от 7, только не ужасы";

    private readonly FakeProviderHandler _handler = new();
    private readonly Queue<Func<HttpResponseMessage>> _answers = new();
    private readonly IHost _host;

    public QueryUnderstandingServiceTests()
    {
        _handler.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal)
            ? _answers.Dequeue()()
            : FakeProviderHandler.DefaultResponse(request);
        _host = AiTestHost.Build(_handler, configureServices: services => services.AddSingleton<ICatalogReader, GenreCatalog>());
    }

    private List<RecordedRequest> ChatRequests => [.. _handler.Requests.Where(r => r.Uri.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal))];

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Valid_answer_becomes_filters_and_the_request_carries_the_schema_and_prompt()
    {
        Answer(Valid);

        var result = await UnderstandAsync(Query);

        result.Source.ShouldBe(QueryUnderstandingSource.Llm);
        result.PromptVersion.ShouldBe(new PromptLibrary().Get(QueryUnderstandingService.PromptName).Id);
        result.SemanticQuery.ShouldBe("space");
        result.Filters.GenresExclude.ShouldBe(["horror"]);
        result.Filters.YearFrom.ShouldBe(1990);
        result.Filters.MinRating.ShouldBe(7);

        using var body = JsonDocument.Parse(ChatRequests.Single().Body);
        var root = body.RootElement;
        var format = root.GetProperty("response_format");
        format.GetProperty("type").GetString().ShouldBe("json_schema");
        var genreEnum = format.GetProperty("json_schema").GetProperty("schema")
            .GetProperty("properties").GetProperty("filters").GetProperty("properties")
            .GetProperty("genresExclude").GetProperty("items").GetProperty("enum");
        genreEnum.EnumerateArray().Select(e => e.GetString()).ShouldBe(["comedy", "horror", "science-fiction"]);
        root.GetProperty("temperature").GetDouble().ShouldBe(0);

        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        messages[0].GetProperty("role").GetString().ShouldBe("system");
        var system = messages[0].GetProperty("content").ToString();
        system.ShouldContain("- horror: Horror / Ужасы / Skräck");
        system.ShouldNotContain("{{");
        system.ShouldNotContain(Query); // the user's text is never part of the instructions
        messages[1].GetProperty("role").GetString().ShouldBe("user");
        messages[1].GetProperty("content").ToString().ShouldContain(Query);
    }

    [Fact]
    public async Task Invalid_answer_is_retried_once_with_the_error_then_the_valid_answer_is_used()
    {
        Answer("""{"semanticQuery":"space","filters":{"genresInclude":["scifi"]},"similarToTitles":[]}""");
        Answer(Valid);

        var result = await UnderstandAsync(Query + " (retry)");

        result.Source.ShouldBe(QueryUnderstandingSource.Llm);
        result.Filters.YearTo.ShouldBe(1999);
        ChatRequests.Count.ShouldBe(2);

        // The second request repeats the conversation, the model's invalid answer and the validation error.
        using var second = JsonDocument.Parse(ChatRequests[1].Body);
        var messages = second.RootElement.GetProperty("messages").EnumerateArray().ToList();
        messages.Count.ShouldBe(4);
        messages[2].GetProperty("role").GetString().ShouldBe("assistant");
        messages[2].GetProperty("content").ToString().ShouldContain("scifi");
        messages[3].GetProperty("role").GetString().ShouldBe("user");
        messages[3].GetProperty("content").ToString().ShouldContain("unknown genre slugs: scifi");
    }

    [Fact]
    public async Task Two_invalid_answers_give_the_fallback()
    {
        Answer("not json at all");
        Answer("""{"semanticQuery":"x","filters":{"yearFrom":2000,"yearTo":1990},"similarToTitles":[]}""");

        var result = await UnderstandAsync("space movies (fallback)");

        result.Source.ShouldBe(QueryUnderstandingSource.Fallback);
        result.SemanticQuery.ShouldBe("space movies (fallback)");
        result.Filters.ShouldBe(new UnderstoodFilters(), new FiltersComparer());
        result.Error.ShouldNotBeNull().ShouldContain("yearFrom is greater than yearTo");
        result.PromptVersion.ShouldBe(new PromptLibrary().Get(QueryUnderstandingService.PromptName).Id);
        ChatRequests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Provider_failure_gives_the_fallback_without_a_retry()
    {
        _answers.Enqueue(() => FakeProviderHandler.Json("""{"error":{"message":"model crashed"}}""", HttpStatusCode.BadRequest));

        var result = await UnderstandAsync("space movies (provider down)");

        result.Source.ShouldBe(QueryUnderstandingSource.Fallback);
        result.Error.ShouldNotBeNull().ShouldContain("provider failed");
        ChatRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Valid_results_are_cached_per_normalized_query_and_fallbacks_are_not()
    {
        Answer(Valid);

        (await UnderstandAsync("Space  movies of the 90s")).Source.ShouldBe(QueryUnderstandingSource.Llm);
        var cached = await UnderstandAsync("  space movies OF the 90s ");

        cached.Source.ShouldBe(QueryUnderstandingSource.Cache);
        cached.Filters.YearFrom.ShouldBe(1990);
        ChatRequests.Count.ShouldBe(1);

        Answer("bad");
        Answer("bad");
        Answer(Valid);
        (await UnderstandAsync("another query")).Source.ShouldBe(QueryUnderstandingSource.Fallback);
        (await UnderstandAsync("another query")).Source.ShouldBe(QueryUnderstandingSource.Llm);
    }

    [Theory]
    [InlineData("  Крёстный   ОТЕЦ ", "крестный отец")]
    [InlineData("Space\tmovies", "space movies")]
    public void Normalize_ignores_case_whitespace_and_yo(string query, string expected) =>
        QueryUnderstandingService.Normalize(query).ShouldBe(expected);

    private void Answer(string content) => _answers.Enqueue(() => FakeProviderHandler.ChatResponse(content));

    private async Task<UnderstoodQuery> UnderstandAsync(string query)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryUnderstanding>()
            .UnderstandAsync(query, TestContext.Current.CancellationToken);
    }

    private sealed class FiltersComparer : IEqualityComparer<UnderstoodFilters>
    {
        public bool Equals(UnderstoodFilters? x, UnderstoodFilters? y) =>
            x is not null && y is not null
            && x.GenresInclude.SequenceEqual(y.GenresInclude) && x.GenresExclude.SequenceEqual(y.GenresExclude)
            && x.YearFrom == y.YearFrom && x.YearTo == y.YearTo && x.MinRating == y.MinRating
            && x.MaxPriceMinor == y.MaxPriceMinor && x.Language == y.Language;

        public int GetHashCode(UnderstoodFilters obj) => 0;
    }

    /// <summary>Only the genres are needed by query understanding.</summary>
    private sealed class GenreCatalog : ICatalogReader
    {
        private static readonly Dictionary<string, (string Slug, string Name)[]> Genres = new()
        {
            ["en"] = [("horror", "Horror"), ("science-fiction", "Science Fiction"), ("comedy", "Comedy")],
            ["ru"] = [("horror", "Ужасы"), ("science-fiction", "Фантастика"), ("comedy", "Комедия")],
            ["sv"] = [("horror", "Skräck"), ("science-fiction", "Science fiction"), ("comedy", "Komedi")],
        };

        public Task<IReadOnlyList<GenreItem>> ListGenresAsync(string locale, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GenreItem>>([.. Genres[locale].Select((g, i) => new GenreItem(i, g.Slug, g.Name))]);

        public Task<PagedResult<MovieSummary>> ListMoviesAsync(MovieListQuery query, string locale, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<MovieSummary>> GetMoviesAsync(IReadOnlyList<Guid> ids, string locale, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MovieDetails?> GetMovieAsync(Guid id, string locale, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<MovieDetails>> GetMovieDetailsAsync(IReadOnlyList<Guid> ids, string locale, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetMovieIdsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasMoviesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
