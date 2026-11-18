using FilmSearch.Core.Ingestion;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Ingestion.Jobs;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace FilmSearch.IntegrationTests;

/// <summary>
/// The job table as a queue (spec 08 §3) on a real PostgreSQL: one active job per kind, <c>SKIP LOCKED</c> claims, leases of
/// lost workers, cancel and release. Each test has its own database and a fake clock (the lease is 60 s).
/// </summary>
public sealed class BackgroundJobsTests(PostgresFixture postgres)
{
    private static readonly TmdbImportRequest Import = new(TmdbIds: [278]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_job_per_kind_can_be_active()
    {
        await using var context = await CreateAsync("jobs_active");

        var first = await context.Store().EnqueueAsync(JobKind.TmdbImport, Import, JobTrigger.Admin, Ct);
        var second = await context.Store().EnqueueAsync(JobKind.TmdbImport, new TmdbImportRequest(TmdbMovieList.Popular), JobTrigger.Schedule, Ct);
        var reindex = await context.Store().EnqueueAsync(JobKind.EmbeddingReindex, new ReindexRequest(), JobTrigger.Admin, Ct);

        first.Created.ShouldBeTrue();
        first.Job.ShouldBe(first.Job with { State = JobState.Queued, Trigger = JobTrigger.Admin, Attempts = 0 });
        first.Job.GetParameters<TmdbImportRequest>().TmdbIds.ShouldBe([278]);
        second.Created.ShouldBeFalse();
        second.Job.Id.ShouldBe(first.Job.Id);
        reindex.Created.ShouldBeTrue();

        // After the first job is finished, the kind has room again.
        var claimed = (await context.Store().ClaimNextAsync("w1", Ct)).ShouldNotBeNull();
        (await context.Store().FinishAsync(claimed.Job.Id, claimed.LeaseId, JobState.Succeeded, null, "{}", null, Ct)).ShouldBeTrue();
        (await context.Store().EnqueueAsync(JobKind.TmdbImport, Import, JobTrigger.Admin, Ct)).Created.ShouldBeTrue();
    }

    [Fact]
    public async Task Two_workers_never_claim_the_same_job()
    {
        await using var context = await CreateAsync("jobs_claim");
        var queued = await context.Store().EnqueueAsync(JobKind.TmdbImport, Import, JobTrigger.Admin, Ct);

        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() => context.Store().ClaimNextAsync($"w{i}", Ct), Ct)));

        var winner = claims.OfType<ClaimedJob>().ShouldHaveSingleItem();
        winner.Job.Id.ShouldBe(queued.Job.Id);
        winner.Job.ShouldBe(winner.Job with { State = JobState.Running, Attempts = 1 });
        winner.Job.WorkerId.ShouldNotBeNull();
        (await context.Store().ClaimNextAsync("late", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_job_of_a_lost_worker_is_claimed_again_until_its_attempts_are_used_up()
    {
        await using var context = await CreateAsync("jobs_lease");
        var queued = await context.Store().EnqueueAsync(JobKind.TmdbImport, Import, JobTrigger.Admin, Ct);

        var first = (await context.Store().ClaimNextAsync("lost", Ct)).ShouldNotBeNull();
        (await context.Store().HeartbeatAsync(first.Job.Id, first.LeaseId, """{"phase":"movies"}""", Ct)).ShouldBe(HeartbeatOutcome.Continue);

        context.Time.Advance(TimeSpan.FromSeconds(30));
        (await context.Store().ClaimNextAsync("other", Ct)).ShouldBeNull("the lease still holds");

        context.Time.Advance(TimeSpan.FromSeconds(31));
        var second = (await context.Store().ClaimNextAsync("other", Ct)).ShouldNotBeNull();
        second.Job.Id.ShouldBe(queued.Job.Id);
        second.Job.Attempts.ShouldBe(2);
        second.Job.WorkerId.ShouldBe("other");
        second.Job.GetProgress<TmdbImportProgress>().ShouldNotBeNull().Phase.ShouldBe(TmdbImportPhase.Movies);

        // The lost worker comes back: its writes are refused.
        (await context.Store().HeartbeatAsync(first.Job.Id, first.LeaseId, null, Ct)).ShouldBe(HeartbeatOutcome.LeaseLost);
        (await context.Store().FinishAsync(first.Job.Id, first.LeaseId, JobState.Succeeded, null, null, null, Ct)).ShouldBeFalse();

        context.Time.Advance(TimeSpan.FromSeconds(61));
        (await context.Store().ClaimNextAsync("third", Ct)).ShouldNotBeNull().Job.Attempts.ShouldBe(3);
        context.Time.Advance(TimeSpan.FromSeconds(61));
        (await context.Store().ClaimNextAsync("fourth", Ct)).ShouldBeNull();

        var failed = (await context.Store().GetAsync(queued.Job.Id, Ct)).ShouldNotBeNull();
        failed.State.ShouldBe(JobState.Failed);
        failed.Error.ShouldBe("The worker running the job stopped sending heartbeats (3 attempts).");
    }

    [Fact]
    public async Task A_queued_job_is_cancelled_at_once_and_a_running_one_at_its_heartbeat()
    {
        await using var context = await CreateAsync("jobs_cancel");
        var reindex = await context.Store().EnqueueAsync(JobKind.EmbeddingReindex, new ReindexRequest(), JobTrigger.Admin, Ct);
        context.Time.Advance(TimeSpan.FromSeconds(1));
        var import = await context.Store().EnqueueAsync(JobKind.TmdbImport, Import, JobTrigger.Admin, Ct);

        var running = (await context.Store().ClaimNextAsync("w1", Ct)).ShouldNotBeNull();
        running.Job.Id.ShouldBe(reindex.Job.Id, "the oldest job first");

        (await context.Store().CancelAsync(import.Job.Id, Ct)).ShouldNotBeNull().State.ShouldBe(JobState.Cancelled);
        var requested = (await context.Store().CancelAsync(running.Job.Id, Ct)).ShouldNotBeNull();
        requested.ShouldBe(requested with { State = JobState.Running, CancelRequested = true });
        (await context.Store().HeartbeatAsync(running.Job.Id, running.LeaseId, null, Ct)).ShouldBe(HeartbeatOutcome.CancelRequested);
        (await context.Store().CancelAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
        (await context.Store().ClaimNextAsync("w2", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_released_job_goes_back_to_the_queue_without_using_an_attempt()
    {
        await using var context = await CreateAsync("jobs_release");
        await context.Store().EnqueueAsync(JobKind.TmdbImport, Import, JobTrigger.Admin, Ct);

        var claimed = (await context.Store().ClaimNextAsync("stopping", Ct)).ShouldNotBeNull();
        await context.Store().ReleaseAsync(claimed.Job.Id, claimed.LeaseId, Ct);

        var again = (await context.Store().ClaimNextAsync("next", Ct)).ShouldNotBeNull();
        again.Job.Id.ShouldBe(claimed.Job.Id);
        again.Job.Attempts.ShouldBe(1);
    }

    private async Task<Context> CreateAsync(string database)
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var factory = new FilmSearchApiFactory(postgres, await postgres.CreateDatabaseAsync(database), time: time);
        using (factory.CreateClient())
        {
            // Starts the host: migrations run.
        }

        return new Context(factory, time);
    }

    private sealed class Context(FilmSearchApiFactory factory, FakeTimeProvider time) : IAsyncDisposable
    {
        private readonly List<AsyncServiceScope> _scopes = [];

        public FakeTimeProvider Time { get; } = time;

        /// <summary>A store in a scope of its own, like one request or one worker iteration.</summary>
        public JobStore Store()
        {
            var scope = factory.Services.CreateAsyncScope();
            lock (_scopes)
            {
                _scopes.Add(scope);
            }

            return scope.ServiceProvider.GetRequiredService<JobStore>();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var scope in _scopes)
            {
                await scope.DisposeAsync();
            }

            await factory.DisposeAsync();
        }
    }
}
