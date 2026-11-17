using System.Net;
using System.Net.Http.Json;
using FilmSearch.Api.Modules.Admin;
using FilmSearch.Api.Modules.Search;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Persistence;
using FilmSearch.Infrastructure.Search.Semantic;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.IntegrationTests;

/// <summary>
/// The embeddings pipeline (FR-DATA-3, FR-DATA-4; spec 08 §4) on a three-movie catalog. Each test has its own database:
/// the tests count vectors and switch models.
/// </summary>
public sealed class EmbeddingIndexTests(PostgresFixture postgres)
{
    private const string LocalModel = "text-embedding-embeddinggemma-300m";
    private const string SecondModel = "fake-384";

    /// <summary>A second embedding profile of another dimension, as an owner would add to try a new model.</summary>
    private static readonly Dictionary<string, string?> SecondProfile = new()
    {
        ["AI:EmbeddingProfiles:second:Endpoint"] = "http://second.test/v1",
        ["AI:EmbeddingProfiles:second:Model"] = SecondModel,
        ["AI:EmbeddingProfiles:second:ApiKey"] = "unused",
        ["AI:EmbeddingProfiles:second:Dimensions"] = "384",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reindex_embeds_only_movies_whose_text_changed()
    {
        await using var factory = await CreateCatalogAsync("emb_stale");
        var ocean = await MovieIdAsync(factory, Ocean.TmdbId);

        // No vectors yet: similar movies are empty, not an error.
        using (var client = factory.CreateClient())
        {
            (await client.GetFromJsonAsync<List<SearchItem>>($"/api/movies/{ocean}/similar", CatalogApiFixture.Json, Ct)).ShouldBeEmpty();
        }

        var first = await ReindexAsync(factory, new ReindexRequest());
        first.ShouldBe(first with { Profile = "local", Model = LocalModel, Dimensions = 768, Movies = 3, Embedded = 3, Unchanged = 0, Deleted = 0 });
        first.Locales.ShouldBe(["en"]);

        var second = await ReindexAsync(factory, new ReindexRequest());
        second.Embedded.ShouldBe(0);
        second.Unchanged.ShouldBe(3);

        var before = await RowsAsync(factory);
        await UpsertAsync(factory, Ocean with
        {
            Translations = new Dictionary<string, TranslationImport>
            {
                ["en"] = new("Ocean Voyage", "A lighthouse keeper rescues sailors from a storm.", null),
            },
        });

        var third = await ReindexAsync(factory, new ReindexRequest());
        third.Embedded.ShouldBe(1);
        third.Unchanged.ShouldBe(2);

        var after = await RowsAsync(factory);
        after[ocean].ContentHash.ShouldNotBe(before[ocean].ContentHash);
        after[ocean].Text.ShouldContain("lighthouse keeper");
        foreach (var other in after.Keys.Where(id => id != ocean))
        {
            after[other].ContentHash.ShouldBe(before[other].ContentHash);
            after[other].CreatedAt.ShouldBe(before[other].CreatedAt);
        }

        // The stored text is exactly what was sent: the profile's document prefix and the versioned template.
        after[ocean].Text.ShouldStartWith("title: none | text: Title: Ocean Voyage (1999)");

        var forced = await ReindexAsync(factory, new ReindexRequest(OnlyStale: false));
        forced.Embedded.ShouldBe(3);
    }

    [Fact]
    public async Task Locales_can_be_added_and_removed()
    {
        await using var factory = await CreateCatalogAsync("emb_locales");

        var all = await ReindexAsync(factory, new ReindexRequest(Locales: ["en", "ru", "sv"]));
        all.Embedded.ShouldBe(9);
        (await StatsAsync(factory)).Where(s => s.Model == LocalModel).Select(s => (s.Locale, s.Count))
            .ShouldBe([("en", 3), ("ru", 3), ("sv", 3)]);

        // A movie with three vectors is still one search result (its best vector counts).
        var result = await SemanticSearchAsync(factory, "thieves enter dreams to plant an idea");
        result.Total.ShouldBe(3);
        result.Items.Select(i => i.Movie.Id).ShouldBeUnique();
        result.Items[0].Movie.Title.ShouldBe("Dream Thieves");

        var english = await ReindexAsync(factory, new ReindexRequest(Locales: ["en"]));
        english.Deleted.ShouldBe(6);
        english.Unchanged.ShouldBe(3);
        (await StatsAsync(factory)).Select(s => s.Locale).ShouldBe(["en"]);
    }

    [Fact]
    public async Task A_second_model_with_another_dimension_lives_side_by_side()
    {
        await using var factory = await CreateCatalogAsync("emb_models", SecondProfile);

        await ReindexAsync(factory, new ReindexRequest());
        var second = await ReindexAsync(factory, new ReindexRequest(Profile: "second"));
        second.ShouldBe(second with { Profile = "second", Model = SecondModel, Dimensions = 384, Embedded = 3 });

        (await StatsAsync(factory)).Select(s => (s.Model, s.Dimensions, s.Count))
            .ShouldBe([(SecondModel, 384, 3), (LocalModel, 768, 3)], ignoreOrder: true);
        (await IndexNamesAsync(factory)).ShouldContain(VectorIndex.Name(384));

        // Search keeps using the active profile's model until the configuration is switched.
        (await SemanticSearchAsync(factory, "thieves enter dreams")).Items[0].Movie.Title.ShouldBe("Dream Thieves");

        await using var switched = new FilmSearchApiFactory(
            postgres,
            _connectionStrings[factory],
            new Dictionary<string, string?>(SecondProfile) { ["AI:ActiveEmbeddingProfile"] = "second" });
        (await SemanticSearchAsync(switched, "thieves enter dreams")).Items[0].Movie.Title.ShouldBe("Dream Thieves");

        using var client = switched.CreateClient();
        using var deleteOld = await client.DeleteAsync($"/api/admin/embeddings?model={LocalModel}", Ct);
        deleteOld.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await deleteOld.Content.ReadFromJsonAsync<DeleteEmbeddingsResponse>(CatalogApiFixture.Json, Ct)).ShouldBe(new DeleteEmbeddingsResponse(LocalModel, 3));

        using var deleteActive = await client.DeleteAsync($"/api/admin/embeddings?model={SecondModel}", Ct);
        deleteActive.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await deleteActive.Content.ReadAsStringAsync(Ct)).ShouldContain("active embedding profile");
    }

    [Fact]
    public async Task Reindex_endpoint_runs_a_background_job()
    {
        await using var factory = await CreateCatalogAsync("emb_job");
        using var client = factory.CreateClient();

        using var started = await client.PostAsJsonAsync("/api/admin/embeddings/reindex", new { onlyStale = true }, Ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var job = (await started.Content.ReadFromJsonAsync<EmbeddingJobStatus>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull();
        job.State.ShouldBeOneOf(EmbeddingJobState.Queued, EmbeddingJobState.Running, EmbeddingJobState.Succeeded);

        var state = await WaitForJobAsync(client, job.Id);
        state.Job!.State.ShouldBe(EmbeddingJobState.Succeeded, state.Job.Error);
        state.Job.Result.ShouldNotBeNull().Embedded.ShouldBe(3);
        state.Job.Progress.ShouldNotBeNull().Processed.ShouldBe(3);
        state.ActiveProfile.ShouldBe("local");
        state.ActiveModel.ShouldBe(LocalModel);
        state.Sets.ShouldHaveSingleItem().ShouldBe(new EmbeddingSetStats(LocalModel, 768, "en", 3));
    }

    [Theory]
    [InlineData("""{ "profile": "nope" }""", "Unknown embedding profile 'nope'")]
    [InlineData("""{ "locales": ["de"] }""", "Unknown locale(s): de")]
    public async Task Reindex_endpoint_validates_the_request(string body, string message)
    {
        await using var factory = new FilmSearchApiFactory(postgres);
        using var client = factory.CreateClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/admin/embeddings/reindex", content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain(message);
    }

    [Fact]
    public async Task Semantic_search_returns_503_and_hybrid_search_degrades_to_keyword_when_the_embedding_provider_fails()
    {
        await using var catalog = await CreateCatalogAsync("emb_down");
        await using var factory = catalog.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>("local", new FailingEmbeddingGenerator())));
        using var client = factory.CreateClient();

        using var semantic = await client.PostAsJsonAsync("/api/search", new { query = "dreams", mode = "semantic" }, Ct);
        using var keyword = await client.PostAsJsonAsync("/api/search", new { query = "dreams", mode = "keyword" }, Ct);
        using var hybrid = await client.PostAsJsonAsync("/api/search", new { query = "dreams", debug = true }, Ct);

        semantic.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await semantic.Content.ReadAsStringAsync(Ct)).ShouldContain("Connection refused");
        keyword.StatusCode.ShouldBe(HttpStatusCode.OK);
        hybrid.StatusCode.ShouldBe(HttpStatusCode.OK);
        var degraded = (await hybrid.Content.ReadFromJsonAsync<SearchResponse>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull();
        degraded.Degraded.ShouldBeTrue();
        degraded.Items.ShouldNotBeEmpty();
        degraded.Items.ShouldAllBe(i => i.Debug!.KeywordRank != null && i.Debug.VectorRank == null);
    }

    private static MovieImport Dreams => Movie(990_601, "Dream Thieves", "Похитители снов", "Thieves enter dreams to plant an idea in a sleeping mind.", 2012);

    private static MovieImport Ocean => Movie(990_602, "Ocean Voyage", "Океанское плавание", "A whale hunt across the stormy ocean.", 1999);

    private static MovieImport Space => Movie(990_603, "Station Mars", "Станция Марс", "Astronauts repair a broken station orbiting Mars.", 2020);

    private static MovieImport Movie(int tmdbId, string title, string titleRu, string overview, int year) => new()
    {
        TmdbId = tmdbId,
        OriginalTitle = title,
        OriginalLanguage = "en",
        ReleaseDate = new DateOnly(year, 1, 1),
        VoteAverage = 7,
        VoteCount = 100,
        GenreIds = [18],
        Translations = new Dictionary<string, TranslationImport>
        {
            ["en"] = new(title, overview, null),
            ["ru"] = new(titleRu, null, null),
        },
    };

    private readonly Dictionary<FilmSearchApiFactory, string> _connectionStrings = [];

    private async Task<FilmSearchApiFactory> CreateCatalogAsync(string database, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var connectionString = await postgres.CreateDatabaseAsync(database);
        var factory = new FilmSearchApiFactory(postgres, connectionString, settings);
        _connectionStrings[factory] = connectionString;
        using (factory.CreateClient())
        {
            // Starts the host: migrations run.
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<ICatalogWriter>();
        await writer.UpsertGenresAsync([new GenreImport(18, "drama", new Dictionary<string, string> { ["en"] = "Drama" })], Ct);
        await writer.UpsertMoviesAsync([Dreams, Ocean, Space], MovieSource.Manual, Ct);
        return factory;
    }

    private static async Task<ReindexResult> ReindexAsync(FilmSearchApiFactory factory, ReindexRequest request)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMovieEmbeddingIndexer>().ReindexAsync(request, progress: null, Ct);
    }

    private static async Task<IReadOnlyList<EmbeddingSetStats>> StatsAsync(FilmSearchApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMovieEmbeddingIndexer>().GetStatsAsync(Ct);
    }

    private static async Task UpsertAsync(FilmSearchApiFactory factory, MovieImport movie)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICatalogWriter>().UpsertMoviesAsync([movie], MovieSource.Manual, Ct);
    }

    private static async Task<Guid> MovieIdAsync(FilmSearchApiFactory factory, int tmdbId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<Movie>().Where(m => m.TmdbId == tmdbId).Select(m => m.Id).SingleAsync(Ct);
    }

    private static async Task<Dictionary<Guid, MovieEmbedding>> RowsAsync(FilmSearchApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<MovieEmbedding>().AsNoTracking().Where(e => e.Model == LocalModel).ToDictionaryAsync(e => e.MovieId, Ct);
    }

    private static async Task<List<string>> IndexNamesAsync(FilmSearchApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database
            .SqlQuery<string>($"SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'search' AND tablename = 'movie_embeddings'")
            .ToListAsync(Ct);
    }

    private static async Task<SearchResponse> SemanticSearchAsync(FilmSearchApiFactory factory, string query)
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/search", new { query, mode = "semantic" }, CatalogApiFixture.Json, Ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<SearchResponse>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull();
    }

    private static async Task<EmbeddingIndexState> WaitForJobAsync(HttpClient client, Guid jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var state = (await client.GetFromJsonAsync<EmbeddingIndexState>("/api/admin/embeddings", CatalogApiFixture.Json, Ct)).ShouldNotBeNull();
            if (state.Job?.Id == jobId && !state.Job.IsActive || DateTime.UtcNow > deadline)
            {
                return state;
            }

            await Task.Delay(100, Ct);
        }
    }

    /// <summary>An embedding endpoint that cannot be reached, like LM Studio when its server is stopped.</summary>
    private sealed class FailingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Connection refused (localhost:1234)");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
