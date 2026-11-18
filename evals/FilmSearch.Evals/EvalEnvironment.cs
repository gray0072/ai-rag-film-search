using FilmSearch.AI.Assistant;
using FilmSearch.AI.Providers;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Ingestion;
using FilmSearch.Core.Owners;
using FilmSearch.Core.Search;
using FilmSearch.Evals.Assistant;
using FilmSearch.Infrastructure.Assistant;
using FilmSearch.Infrastructure.Catalog;
using FilmSearch.Infrastructure.Commerce;
using FilmSearch.Infrastructure.Persistence;
using FilmSearch.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace FilmSearch.Evals;

/// <summary>
/// The services the suites run against: the API's configuration (<c>src/FilmSearch.Api/appsettings*.json</c> + API user
/// secrets) and the same module registrations as the API (catalog, search, AI providers, commerce and the assistant with
/// an owner set by the suite instead of a cookie; no payment gateway), over a database with the seed
/// loaded and embedded. By default the database is a fresh pgvector container, so every run starts from the same data.
/// The host is built but not started: no background jobs run.
/// </summary>
public sealed class EvalEnvironment : IAsyncDisposable
{
    /// <summary>Same image as the AppHost and the integration tests.</summary>
    public const string PostgresImage = "pgvector/pgvector:pg17";

    private readonly PostgreSqlContainer? _container;
    private readonly IHost _host;

    /// <summary>Name of the profile <c>--judge-model</c> creates: a copy of the judge's source profile with another model.</summary>
    public const string JudgeProfileName = "eval-judge";

    private EvalEnvironment(PostgreSqlContainer? container, IHost host, IConfiguration configuration, bool freshDatabase, string judgeProfile)
    {
        _container = container;
        _host = host;
        Configuration = configuration;
        FreshDatabase = freshDatabase;
        JudgeProfile = judgeProfile;
    }

    /// <summary>Chat profile of the LLM judge (assistant suites).</summary>
    public string JudgeProfile { get; }

    public IServiceProvider Services => _host.Services;

    public IConfiguration Configuration { get; }

    /// <summary>True when the run uses its own container (not <c>--connection</c>).</summary>
    public bool FreshDatabase { get; }

    public static async Task<EvalEnvironment> StartAsync(EvalOptions options, string repositoryRoot, CancellationToken cancellationToken)
    {
        PostgreSqlContainer? container = null;
        var connectionString = options.Connection;
        if (connectionString is null)
        {
            Console.Error.WriteLine($"Starting a {PostgresImage} container...");
            container = new PostgreSqlBuilder(PostgresImage).WithDatabase("filmsearch").Build();
            await container.StartAsync(cancellationToken);
            connectionString = container.GetConnectionString();
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = Path.Combine(repositoryRoot, "src", "FilmSearch.Api"),
            // The assembly with the API's UserSecretsId: user secrets load in Development.
            ApplicationName = typeof(EvalEnvironment).Assembly.GetName().Name,
        });

        var settings = new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Warning",
            // A fresh database has no migrations history table yet: EF logs the failed probe as an error. Failed
            // commands still throw.
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "None",
            // Tool refusals and grounding warnings of the assistant are in the report; the console stays readable.
            ["Logging:LogLevel:FilmSearch.AI.Assistant"] = "Error",
            [$"ConnectionStrings:{DatabaseSetup.ConnectionName}"] = connectionString,
            ["Catalog:Seed:FilePath"] = Path.Combine(repositoryRoot, "data", "seed", "movies.seed.json"),
            ["AI:HealthChecksEnabled"] = "false",
            ["AI:LogRawHttp"] = "false",
        };
        if (options.EmbeddingProfile is not null)
        {
            settings["AI:ActiveEmbeddingProfile"] = options.EmbeddingProfile;
        }

        if (options.ChatProfile is not null)
        {
            settings["AI:ActiveChatProfile"] = options.ChatProfile;
        }

        foreach (var (key, value) in options.Overrides)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);

        // The judge: a named profile, or a copy of one with another model (same endpoint and key).
        var judgeProfile = options.JudgeProfile ?? builder.Configuration[$"{AiOptions.SectionName}:{nameof(AiOptions.ActiveChatProfile)}"] ?? "";
        if (options.JudgeModel is not null)
        {
            var source = builder.Configuration.GetSection($"{AiOptions.SectionName}:{nameof(AiOptions.ChatProfiles)}:{judgeProfile}");
            var copy = source.AsEnumerable(makePathsRelative: true)
                .Where(p => p.Value is not null)
                .ToDictionary(p => $"{AiOptions.SectionName}:{nameof(AiOptions.ChatProfiles)}:{JudgeProfileName}:{p.Key}", p => p.Value);
            copy[$"{AiOptions.SectionName}:{nameof(AiOptions.ChatProfiles)}:{JudgeProfileName}:{nameof(ChatProfileOptions.Model)}"] = options.JudgeModel;
            builder.Configuration.AddInMemoryCollection(copy);
            judgeProfile = JudgeProfileName;
        }

        // The same registrations as Program.cs of the API (AddCatalogModule / AddSearchModule delegate to these).
        builder.AddDatabase();
        builder.AddAiProviders();
        builder.Services.AddCatalogInfrastructure(builder.Configuration);
        builder.Services.AddSearchInfrastructure(builder.Configuration);

        // Commerce and the assistant (AddCommerceModule / AddAssistantModule). The owner comes from the suite, not a cookie.
        builder.Services.AddScoped<IOwnerContext, EvalOwnerContext>();
        builder.Services.AddCommerceInfrastructure(builder.Configuration);
        builder.Services.AddAssistantInfrastructure();
        builder.Services.AddAssistant(builder.Configuration);

        var host = builder.Build();
        return new EvalEnvironment(container, host, builder.Configuration, container is not null, judgeProfile);
    }

    /// <summary>Applies migrations and loads the seed when the catalog is empty.</summary>
    public async Task PrepareCatalogAsync(CancellationToken cancellationToken)
    {
        await Services.MigrateDatabaseAsync(cancellationToken);
        await using var scope = Services.CreateAsyncScope();
        var seeded = await scope.ServiceProvider.GetRequiredService<ICatalogSeeder>().LoadIfEmptyAsync(cancellationToken);
        if (seeded is not null)
        {
            Console.Error.WriteLine($"Seed loaded: {seeded.Created} movies in {seeded.ElapsedMilliseconds} ms.");
        }
    }

    /// <summary>Embeds the movies that have no current vector of the active embedding profile.</summary>
    public async Task<ReindexResult> PrepareEmbeddingsAsync(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        var indexer = scope.ServiceProvider.GetRequiredService<IMovieEmbeddingIndexer>();
        var lastReported = 0;
        var progress = new Progress<ReindexProgress>(p =>
        {
            if (p.Processed - lastReported >= 200 || p.Processed == p.Total)
            {
                lastReported = p.Processed;
                Console.Error.WriteLine($"Embeddings: {p.Processed}/{p.Total} checked, {p.Embedded} embedded");
            }
        });

        var result = await indexer.ReindexAsync(new ReindexRequest(), progress, cancellationToken);
        Console.Error.WriteLine($"Embeddings ready: {result.Model}, {result.Embedded} embedded, {result.Unchanged} unchanged in {result.ElapsedMilliseconds} ms.");
        return result;
    }

    /// <summary>Catalog id → TMDB id of every movie: datasets refer to TMDB ids.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> GetTmdbIdsAsync(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Set<Movie>()
            .Where(m => m.TmdbId != null)
            .Select(m => new { m.Id, m.TmdbId })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => r.TmdbId!.Value);
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
