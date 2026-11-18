extern alias worker;

using FilmSearch.AI.Providers;
using FilmSearch.Infrastructure.Persistence;
using FilmSearch.Infrastructure.Tmdb;
using FilmSearch.Tests.Fixtures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WorkerHost = worker::FilmSearch.Worker.WorkerHost;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>
/// The Worker in-process with its real composition (<see cref="WorkerHost"/>) against a test database: TMDB is answered by
/// <see cref="Tmdb"/> (recorded responses), embeddings by <see cref="HashingEmbeddingGenerator"/>, the queue is polled every
/// 100 ms. Order expiry is off. Pair it with a <see cref="FilmSearchApiFactory"/> on the same connection string.
/// </summary>
public sealed class TestWorker : IAsyncDisposable
{
    private readonly IHost _host;

    private TestWorker(IHost host, TmdbStub tmdb)
    {
        _host = host;
        Tmdb = tmdb;
    }

    public TmdbStub Tmdb { get; }

    public IServiceProvider Services => _host.Services;

    public static async Task<TestWorker> StartAsync(string connectionString, TmdbStub? tmdb = null, IReadOnlyDictionary<string, string?>? settings = null)
    {
        tmdb ??= new TmdbStub();
        var builder = WorkerHost.CreateBuilder([], "Testing");
        var values = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{DatabaseSetup.ConnectionName}"] = connectionString,
            [DatabaseSetup.MigrateOnStartupKey] = "true",
            ["AI:HealthChecksEnabled"] = "false",
            ["Jobs:PollIntervalMs"] = "100",
            ["Jobs:HeartbeatIntervalMs"] = "100",
            ["Commerce:ExpiryScanSeconds"] = "0",
            ["Tmdb:ApiKey"] = "0123456789abcdef0123456789abcdef",
            ["Tmdb:RetryBaseDelayMs"] = "10",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(values);
        WorkerHost.AddWorkerServices(builder);

        // Never a real model or the real TMDB API from tests (AGENTS.md).
        builder.Services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(KeyedService.AnyKey, (sp, key) =>
            new HashingEmbeddingGenerator(sp.GetRequiredService<IOptions<AiOptions>>().Value.GetEmbeddingProfile((string)key!).Dimensions));
        builder.Services.AddKeyedSingleton<IChatClient>(KeyedService.AnyKey, new ScriptedChatClient());
        builder.Services.AddHttpClient(TmdbClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => tmdb);

        var host = builder.Build();
        await WorkerHost.MigrateOnStartupAsync(host);
        await host.StartAsync(TestContext.Current.CancellationToken);
        return new TestWorker(host, tmdb);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync(CancellationToken.None);
        _host.Dispose();
    }
}
