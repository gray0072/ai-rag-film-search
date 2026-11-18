using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FilmSearch.Api.Modules.Search;
using FilmSearch.Core.Ingestion;
using FilmSearch.IntegrationTests.Infrastructure;
using FilmSearch.Tests.Fixtures;

namespace FilmSearch.IntegrationTests;

/// <summary>
/// TMDB import end to end (FR-DATA-2, spec 08 §3): the admin endpoint queues a job, the Worker (in-process, real
/// composition) imports recorded TMDB responses, embeds, and the movies are searchable when the job ends. Own database per test.
/// </summary>
public sealed class WorkerTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_list_import_makes_new_movies_searchable_and_a_rerun_changes_nothing()
    {
        var connectionString = await postgres.CreateDatabaseAsync("worker_import");
        await using var api = new FilmSearchApiFactory(postgres, connectionString);
        using var client = api.CreateClient();
        await using var worker = await TestWorker.StartAsync(connectionString);

        var first = await RunImportAsync(client, new { list = "topRated", pages = 1 });

        first.State.ShouldBe(JobState.Succeeded, first.Error);
        first.Trigger.ShouldBe(JobTrigger.Admin);
        first.Attempts.ShouldBe(1);
        var result = first.GetResult<TmdbImportResult>().ShouldNotBeNull();
        result.ShouldBe(result with { Movies = 3, Created = 3, Updated = 0, Unchanged = 0, Skipped = 0, NotFound = 0, Failed = 0 });
        result.Embeddings.ShouldNotBeNull().Embedded.ShouldBe(3);
        first.GetProgress<TmdbImportProgress>().ShouldNotBeNull().Phase.ShouldBe(TmdbImportPhase.Embeddings);

        // Searchable right away in every mode: keyword documents in the upsert, vectors by the reindex at the end of the job.
        (await SearchAsync(client, "Shawshank", "keyword")).ShouldContain("The Shawshank Redemption");
        (await SearchAsync(client, "Shawshank Redemption", "semantic")).ShouldContain("The Shawshank Redemption");
        (await SearchAsync(client, "Pulp Fiction", "hybrid")).ShouldContain("Pulp Fiction");

        var rerun = await RunImportAsync(client, new { list = "topRated", pages = 1 });
        var again = rerun.GetResult<TmdbImportResult>().ShouldNotBeNull();
        again.ShouldBe(again with { Movies = 3, Created = 0, Updated = 0, Unchanged = 3 });
        again.Embeddings.ShouldNotBeNull().Embedded.ShouldBe(0);
        worker.Tmdb.Count("/3/movie/278").ShouldBe(2);
        worker.Tmdb.Count("/3/movie/999999001").ShouldBe(0, "adult list items are not requested");

        using var list = await client.GetAsync("/api/admin/jobs?kind=TMDBIMPORT&limit=5", Ct);
        (await list.Content.ReadFromJsonAsync<List<JobStatus>>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull()
            .Select(j => j.Id).ShouldBe([rerun.Id, first.Id]);
    }

    [Fact]
    public async Task An_import_by_ids_updates_and_re_embeds_a_changed_movie()
    {
        var connectionString = await postgres.CreateDatabaseAsync("worker_ids");
        await using var api = new FilmSearchApiFactory(postgres, connectionString);
        using var client = api.CreateClient();
        await using var worker = await TestWorker.StartAsync(connectionString);

        var first = (await RunImportAsync(client, new { tmdbIds = new List<int> { 278, 550, 424242 } })).GetResult<TmdbImportResult>().ShouldNotBeNull();
        first.ShouldBe(first with { Movies = 3, Created = 2, NotFound = 1 });

        var changed = JsonNode.Parse(TmdbStub.Recorded("movie-550"))!;
        changed["overview"] = "A lighthouse keeper starts an underground boxing club.";
        worker.Tmdb.Override("/3/movie/550", changed.ToJsonString());

        var second = (await RunImportAsync(client, new { tmdbIds = new List<int> { 278, 550 } })).GetResult<TmdbImportResult>().ShouldNotBeNull();
        second.ShouldBe(second with { Created = 0, Updated = 1, Unchanged = 1 });
        second.Embeddings.ShouldNotBeNull().Embedded.ShouldBe(1);
        (await SearchAsync(client, "lighthouse keeper", "keyword")).ShouldContain("Fight Club");
    }

    [Fact]
    public async Task A_second_import_is_refused_while_one_is_queued_and_bad_requests_are_400()
    {
        // No worker: the job stays queued.
        await using var api = new FilmSearchApiFactory(postgres, await postgres.CreateDatabaseAsync("worker_conflict"));
        using var client = api.CreateClient();

        using var first = await client.PostAsJsonAsync("/api/admin/ingestion/tmdb", new { tmdbIds = new List<int> { 278 } }, Ct);
        using var second = await client.PostAsJsonAsync("/api/admin/ingestion/tmdb", new { list = "popular", pages = 2 }, Ct);
        using var both = await client.PostAsJsonAsync("/api/admin/ingestion/tmdb", new { list = "popular", tmdbIds = new List<int> { 1 } }, Ct);
        using var pages = await client.PostAsJsonAsync("/api/admin/ingestion/tmdb", new { list = "popular", pages = 501 }, Ct);
        using var kind = await client.GetAsync("/api/admin/jobs?kind=nope", Ct);
        using var missing = await client.GetAsync($"/api/admin/jobs/{Guid.NewGuid()}", Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var queued = (await first.Content.ReadFromJsonAsync<JobStatus>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull();
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.Content.ReadFromJsonAsync<JobStatus>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull().Id.ShouldBe(queued.Id);
        both.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        pages.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        kind.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var cancel = await client.PostAsync($"/api/admin/jobs/{queued.Id}/cancel", null, Ct);
        (await cancel.Content.ReadFromJsonAsync<JobStatus>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull().State.ShouldBe(JobState.Cancelled);
    }

    [Fact]
    public async Task Without_a_TMDB_key_the_job_fails_with_a_clear_message()
    {
        var connectionString = await postgres.CreateDatabaseAsync("worker_nokey");
        await using var api = new FilmSearchApiFactory(postgres, connectionString);
        using var client = api.CreateClient();
        await using var worker = await TestWorker.StartAsync(connectionString, settings: new Dictionary<string, string?> { ["Tmdb:ApiKey"] = "" });

        var job = await RunImportAsync(client, new { tmdbIds = new List<int> { 278 } });

        job.State.ShouldBe(JobState.Failed);
        job.Error.ShouldNotBeNull().ShouldContain("Tmdb:ApiKey");
        worker.Tmdb.Requests.ShouldBeEmpty();
    }

    private static async Task<JobStatus> RunImportAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/admin/ingestion/tmdb", body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var job = (await response.Content.ReadFromJsonAsync<JobStatus>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull();
        return await WaitForJobAsync(client, job.Id);
    }

    internal static async Task<JobStatus> WaitForJobAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var job = (await client.GetFromJsonAsync<JobStatus>($"/api/admin/jobs/{id}", CatalogApiFixture.Json, Ct)).ShouldNotBeNull();
            if (!job.IsActive)
            {
                return job;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Job {id} is still {job.State}.");
            }

            await Task.Delay(100, Ct);
        }
    }

    private static async Task<List<string>> SearchAsync(HttpClient client, string query, string mode)
    {
        using var response = await client.PostAsJsonAsync("/api/search", new { query, mode }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var result = (await response.Content.ReadFromJsonAsync<SearchResponse>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull();
        return [.. result.Items.Select(i => i.Movie.Title)];
    }
}
