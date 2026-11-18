using System.Text.Json;
using FilmSearch.Core.Ingestion;
using FilmSearch.Infrastructure.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FilmSearch.UnitTests.Tmdb;

/// <summary>The daily TMDB import (spec 08 §3): queued once per day after the configured time, also across restarts.</summary>
public sealed class TmdbImportSchedulerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Queues_one_import_per_day_after_the_configured_time()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 2, 30, 0, TimeSpan.Zero));
        var jobs = new FakeJobs(time);
        var settings = new TmdbScheduleOptions { Enabled = true, TimeUtc = new TimeOnly(3, 0), List = TmdbMovieList.Popular, Pages = 5 };
        var scheduler = Create(jobs, settings, time);

        await scheduler.QueueIfDueAsync(settings, Ct);
        jobs.Queued.ShouldBeEmpty();

        time.Advance(TimeSpan.FromMinutes(31));
        await scheduler.QueueIfDueAsync(settings, Ct);
        await scheduler.QueueIfDueAsync(settings, Ct);
        jobs.Queued.ShouldHaveSingleItem().Parameters.ShouldBe(new TmdbImportRequest(TmdbMovieList.Popular, 5));

        // A restarted Worker asks the job table, not its memory.
        await Create(jobs, settings, time).QueueIfDueAsync(settings, Ct);
        jobs.Queued.Count.ShouldBe(1);

        time.Advance(TimeSpan.FromDays(1));
        await scheduler.QueueIfDueAsync(settings, Ct);
        jobs.Queued.Count.ShouldBe(2);
    }

    private static TmdbImportScheduler Create(FakeJobs jobs, TmdbScheduleOptions settings, TimeProvider time)
    {
        var services = new ServiceCollection().AddSingleton<IBackgroundJobs>(jobs).BuildServiceProvider();
        return new TmdbImportScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(settings),
            time,
            NullLogger<TmdbImportScheduler>.Instance);
    }

    private sealed class FakeJobs(TimeProvider time) : IBackgroundJobs
    {
        public List<(JobTrigger Trigger, DateTimeOffset QueuedAt, TmdbImportRequest Parameters)> Queued { get; } = [];

        public Task<EnqueueResult> EnqueueAsync(JobKind kind, object parameters, JobTrigger trigger, CancellationToken cancellationToken)
        {
            Queued.Add((trigger, time.GetUtcNow(), (TmdbImportRequest)parameters));
            var job = new JobStatus
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                State = JobState.Queued,
                Trigger = trigger,
                Parameters = JsonSerializer.SerializeToElement(parameters, JobJson.Options),
                QueuedAt = time.GetUtcNow(),
            };
            return Task.FromResult(new EnqueueResult(true, job));
        }

        public Task<bool> AnyQueuedSinceAsync(JobKind kind, JobTrigger trigger, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult(Queued.Any(q => q.Trigger == trigger && q.QueuedAt >= since));

        public Task<JobStatus?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<JobStatus>> ListAsync(JobKind? kind, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<JobStatus?> CancelAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
