using System.Net;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Ingestion;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Ingestion;
using FilmSearch.Infrastructure.Tmdb;
using FilmSearch.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FilmSearch.UnitTests.Tmdb;

/// <summary>The TMDB import pipeline (FR-DATA-2, spec 08 §3) over recorded responses, with a fake catalog writer and indexer.</summary>
public sealed class TmdbImporterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Imports_a_list_page_with_genres_in_every_locale_then_embeds()
    {
        var harness = new Harness(new TmdbStub());
        var progress = new List<TmdbImportProgress>();

        var result = await harness.Importer.ImportAsync(new TmdbImportRequest(TmdbMovieList.TopRated, Pages: 1), new SyncProgress<TmdbImportProgress>(progress.Add), Ct);

        // The adult list item is not requested at all.
        result.ShouldBe(result with { Movies = 3, Created = 3, Updated = 0, Unchanged = 0, Skipped = 0, NotFound = 0, Failed = 0 });
        result.Embeddings.ShouldNotBeNull().Embedded.ShouldBe(3);
        harness.Writer.Movies.Select(m => m.TmdbId).ShouldBe([278, 550, 680], ignoreOrder: true);
        harness.Writer.Sources.ShouldAllBe(s => s == MovieSource.Tmdb);

        var drama = harness.Writer.Genres.Single(g => g.Id == 18);
        drama.Slug.ShouldBe("drama");
        drama.Names["ru"].ShouldBe("Драма");
        drama.Names["sv"].ShouldNotBeNullOrWhiteSpace();

        var shawshank = harness.Writer.Movies.Single(m => m.TmdbId == 278);
        shawshank.Translations["ru"].Title.ShouldBe("Побег из Шоушенка");
        shawshank.Credits.ShouldContain(c => c.Role == CreditRole.Director);

        progress.Select(p => p.Phase).Distinct().ShouldBe([TmdbImportPhase.Genres, TmdbImportPhase.Listing, TmdbImportPhase.Movies, TmdbImportPhase.Embeddings]);
        progress.Last().Embeddings.ShouldNotBeNull();
        harness.Indexer.Requests.ShouldHaveSingleItem().ShouldBe(new ReindexRequest());
    }

    [Fact]
    public async Task Unknown_and_failing_ids_are_counted_and_do_not_stop_the_others()
    {
        var stub = new TmdbStub().Script("/3/movie/550", () => TmdbStub.Status(HttpStatusCode.Unauthorized));
        var harness = new Harness(stub);

        var result = await harness.Importer.ImportAsync(new TmdbImportRequest(TmdbIds: [278, 550, 1, 278]), null, Ct);

        result.ShouldBe(result with { Movies = 3, Created = 1, NotFound = 1, Failed = 1 });
        result.Errors.ShouldHaveSingleItem().ShouldStartWith("550: TMDB movie/550");
        harness.Writer.Movies.ShouldHaveSingleItem().TmdbId.ShouldBe(278);
    }

    [Fact]
    public async Task An_embedding_failure_fails_the_import_with_the_saved_counters()
    {
        var harness = new Harness(new TmdbStub()) { Indexer = { Failure = new InvalidOperationException("Connection refused.") } };

        var error = await Should.ThrowAsync<TmdbImportException>(() =>
            harness.Importer.ImportAsync(new TmdbImportRequest(TmdbIds: [278]), null, Ct));

        error.Message.ShouldContain("The movies were saved, but embedding them failed: Connection refused.");
        error.Result.ShouldNotBeNull().Created.ShouldBe(1);
        error.Result.Embeddings.ShouldBeNull();
    }

    [Fact]
    public async Task When_every_movie_fails_the_import_fails_before_embedding()
    {
        var stub = new TmdbStub().Script("/3/movie/278", () => TmdbStub.Status(HttpStatusCode.Forbidden));
        var harness = new Harness(stub);

        var error = await Should.ThrowAsync<TmdbImportException>(() =>
            harness.Importer.ImportAsync(new TmdbImportRequest(TmdbIds: [278]), null, Ct));

        error.Message.ShouldStartWith("Every movie request failed. First error: 278: TMDB movie/278");
        harness.Indexer.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, 1, new int[0], "either a list")]
    [InlineData(TmdbMovieList.Popular, 1, new[] { 278 }, "either a list")]
    [InlineData(TmdbMovieList.Popular, 501, new int[0], "Pages must be between 1 and 500")]
    [InlineData(null, 1, new[] { -5 }, "must be positive")]
    public async Task Invalid_requests_are_refused_without_a_request(TmdbMovieList? list, int pages, int[] ids, string message)
    {
        var stub = new TmdbStub();
        var harness = new Harness(stub);

        var error = await Should.ThrowAsync<TmdbImportException>(() =>
            harness.Importer.ImportAsync(new TmdbImportRequest(list, pages, ids), null, Ct));

        error.Message.ShouldContain(message);
        stub.Requests.ShouldBeEmpty();
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _services;

        public Harness(TmdbStub stub)
        {
            _services = TmdbClientTests.Create(stub);
            Importer = new TmdbImporter(
                _services.GetRequiredService<TmdbClient>(),
                Writer,
                Indexer,
                _services.GetRequiredService<IOptions<TmdbOptions>>(),
                NullLogger<TmdbImporter>.Instance);
        }

        public FakeCatalogWriter Writer { get; } = new();

        public FakeIndexer Indexer { get; } = new();

        public TmdbImporter Importer { get; }

        public void Dispose() => _services.Dispose();
    }

    private sealed class FakeCatalogWriter : ICatalogWriter
    {
        private readonly HashSet<int> _known = [];

        public List<GenreImport> Genres { get; } = [];

        public List<MovieImport> Movies { get; } = [];

        public List<MovieSource> Sources { get; } = [];

        public Task UpsertGenresAsync(IReadOnlyList<GenreImport> genres, CancellationToken cancellationToken)
        {
            Genres.AddRange(genres);
            return Task.CompletedTask;
        }

        public Task<UpsertResult> UpsertMoviesAsync(IReadOnlyList<MovieImport> movies, MovieSource source, CancellationToken cancellationToken)
        {
            Movies.AddRange(movies);
            Sources.Add(source);
            var created = movies.Count(m => _known.Add(m.TmdbId));
            return Task.FromResult(new UpsertResult(created, 0, movies.Count - created));
        }
    }

    private sealed class FakeIndexer : IMovieEmbeddingIndexer
    {
        public List<ReindexRequest> Requests { get; } = [];

        public Exception? Failure { get; set; }

        public Task<ReindexResult> ReindexAsync(ReindexRequest request, IProgress<ReindexProgress>? progress, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Failure is not null)
            {
                throw Failure;
            }

            progress?.Report(new ReindexProgress(3, 3, 3, 0));
            return Task.FromResult(new ReindexResult("local", "fake", 8, ["en"], 3, 3, 0, 0, 1));
        }

        public Task<IReadOnlyList<EmbeddingSetStats>> GetStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> DeleteModelAsync(string model, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        private readonly Lock _lock = new();

        public void Report(T value)
        {
            lock (_lock)
            {
                report(value);
            }
        }
    }
}
