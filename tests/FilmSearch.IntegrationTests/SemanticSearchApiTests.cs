using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using FilmSearch.Api.Modules.Search;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Persistence;
using FilmSearch.Infrastructure.Search.Semantic;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FilmSearch.IntegrationTests;

/// <summary>
/// Semantic search and similar movies over the committed seed (FR-SRCH-2, FR-SRCH-5, FR-SRCH-6; spec 05 §3), embedded
/// with the fake bag-of-words model: these tests check the mechanics (vectors, cosine order, pre-filtering, paging,
/// the HNSW plan), not the quality of a real model, which is checked by hand and in I08.
/// </summary>
public sealed class SemanticSearchApiTests(CatalogApiFixture fixture) : IClassFixture<CatalogApiFixture>, IAsyncLifetime
{
    private static readonly string[] Crime = ["crime"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.EnsureEmbeddingsAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Finds_a_movie_by_its_plot_words_with_vector_debug_scores()
    {
        var result = await SearchAsync(new { query = "a heist inside dreams with a kidnapping mission", mode = "semantic", debug = true });

        result.Items.Take(3).Select(i => i.Movie.Title).ShouldContain("Inception");
        var first = result.Items[0];
        var debug = first.Debug.ShouldNotBeNull();
        debug.VectorRank.ShouldBe(1);
        debug.Similarity.ShouldNotBeNull().ShouldBeInRange(0.0, 1.0);
        first.Score.ShouldBe(debug.Similarity.Value);
        debug.KeywordRank.ShouldBeNull();
        debug.KeywordScore.ShouldBeNull();

        // Best first.
        result.Items.Select(i => i.Score).ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public async Task List_is_capped_and_pages_continue_the_ranking()
    {
        var all = await SearchAsync(new { query = "gangster family", mode = "semantic", pageSize = 10, debug = true });
        var first = await SearchAsync(new { query = "gangster family", mode = "semantic", pageSize = 5, page = 1, debug = true });
        var second = await SearchAsync(new { query = "gangster family", mode = "semantic", pageSize = 5, page = 2, debug = true });

        all.Total.ShouldBe(new SemanticSearchOptions().MaxResults);
        first.Items.Concat(second.Items).Select(i => i.Movie.Id).ShouldBe(all.Items.Select(i => i.Movie.Id));
        second.Items.Select(i => i.Debug!.VectorRank).ShouldBe([6, 7, 8, 9, 10]);
    }

    [Fact]
    public async Task Filters_are_applied_inside_the_vector_query()
    {
        // Pre-filtering: every Swedish-language movie comes back for any query, although the query is about something
        // else. Filtering the top 100 afterwards would return almost none.
        using var client = fixture.CreateClient("en");
        var swedish = await client.GetFromJsonAsync<PagedResult<MovieSummary>>("/api/movies?language=sv&pageSize=50", CatalogApiFixture.Json, Ct);
        swedish.ShouldNotBeNull().Total.ShouldBeGreaterThan(0);

        var result = await SearchAsync(new { query = "space adventure", mode = "semantic", pageSize = 50, filters = new { language = "sv" } });

        result.Total.ShouldBe(swedish.Total);
        result.Items.Select(i => i.Movie.Id).ShouldBe(swedish.Items.Select(m => m.Id), ignoreOrder: true);
        result.AppliedFilters.Language.ShouldBe("sv");
    }

    [Fact]
    public async Task Filters_combine_with_ranking()
    {
        var result = await SearchAsync(new
        {
            query = "gangster family",
            mode = "semantic",
            filters = new { yearFrom = 1980, genres = Crime, minRating = 7 },
        });

        result.Items.ShouldNotBeEmpty();
        result.Items.ShouldAllBe(i => i.Movie.Year >= 1980 && i.Movie.VoteAverage >= 7 && i.Movie.Genres.Any(g => g.Slug == "crime"));
        result.Items.Select(i => i.Movie.Title).ShouldNotContain("The Godfather"); // 1972
    }

    [Fact]
    public async Task Similar_movies_are_vector_neighbours_without_the_movie_itself()
    {
        var godfather = await fixture.FindMovieIdAsync("The Godfather", Ct);
        using var client = fixture.CreateClient("en");

        var similar = await client.GetFromJsonAsync<List<SearchItem>>($"/api/movies/{godfather}/similar?limit=5", CatalogApiFixture.Json, Ct);

        similar.ShouldNotBeNull().Count.ShouldBe(5);
        similar.Select(i => i.Movie.Id).ShouldNotContain(godfather);
        similar.Select(i => i.Movie.Title).ShouldContain("The Godfather Part II");
        similar.Select(i => i.Score).ShouldBeInOrder(SortDirection.Descending);
        similar.ShouldAllBe(i => i.Debug == null);
    }

    [Theory]
    [InlineData("?limit=0", HttpStatusCode.BadRequest)]
    [InlineData("?limit=51", HttpStatusCode.BadRequest)]
    public async Task Similar_movies_validate_the_limit(string query, HttpStatusCode expected)
    {
        var godfather = await fixture.FindMovieIdAsync("The Godfather", Ct);
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync($"/api/movies/{godfather}/similar{query}", Ct);

        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task Similar_movies_of_an_unknown_movie_return_404()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync($"/api/movies/{Guid.NewGuid()}/similar", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Vector_query_can_use_the_hnsw_index()
    {
        // The plan of the real query (raw SQL + LINQ composition), captured from the command EF Core sends. The partial
        // expression index is used only when the query repeats its expression and predicate, through EF Core's subquery.
        // Sequential scans and sorts are switched off for the second EXPLAIN: on ~1000 rows the planner rightly prefers an exact scan
        // (see the I06 experiment), but the index must be usable.
        var explain = new ExplainVectorQueryInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseSetup.Configure(options, fixture.Factory.Services.GetRequiredService<IConfiguration>().GetConnectionString(DatabaseSetup.ConnectionName));
        options.AddInterceptors(explain);
        await using var db = new AppDbContext(options.Options);
        var embedder = fixture.Factory.Services.GetRequiredService<ITextEmbedder>();
        var retriever = new SemanticRetriever(db, embedder, Options.Create(new SemanticSearchOptions()));

        var hits = await retriever.SearchAsync("heist inside dreams", MovieFilter.None, Ct);

        hits.ShouldNotBeEmpty();
        TestContext.Current.SendDiagnosticMessage("Natural plan:\n{0}\nWith enable_seqscan and enable_sort off:\n{1}", explain.NaturalPlan, explain.IndexPlan);
        explain.IndexPlan.ShouldNotBeNull().ShouldContain($"Index Scan using {VectorIndex.Name(embedder.Active.Dimensions)}");
    }

    private async Task<SearchResponse> SearchAsync(object body, string locale = "en")
    {
        using var client = fixture.CreateClient(locale);
        using var response = await client.PostAsJsonAsync("/api/search", body, CatalogApiFixture.Json, Ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<SearchResponse>(CatalogApiFixture.Json, Ct)).ShouldNotBeNull();
    }

    /// <summary>
    /// Runs <c>EXPLAIN</c> of the vector query on the same connection and transaction, with the same parameters, before
    /// EF Core runs it: once as is, once with sequential scans and sorts off (only an index that returns rows in distance order is left).
    /// </summary>
    private sealed class ExplainVectorQueryInterceptor : DbCommandInterceptor
    {
        public string? NaturalPlan { get; private set; }

        public string? IndexPlan { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("<=>", StringComparison.Ordinal))
            {
                var connection = (NpgsqlConnection)command.Connection!;
                var transaction = (NpgsqlTransaction?)command.Transaction;
                NaturalPlan = await ExplainAsync(command, connection, transaction, cancellationToken);
                await ExecuteAsync("SET enable_seqscan = off; SET enable_sort = off", connection, transaction, cancellationToken);
                IndexPlan = await ExplainAsync(command, connection, transaction, cancellationToken);
                await ExecuteAsync("RESET enable_seqscan; RESET enable_sort", connection, transaction, cancellationToken);
            }

            return result;
        }

        private static async Task<string> ExplainAsync(DbCommand command, NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
        {
            await using var explain = new NpgsqlCommand("EXPLAIN " + command.CommandText, connection, transaction);
            foreach (NpgsqlParameter parameter in command.Parameters)
            {
                explain.Parameters.Add(parameter.Clone());
            }

            var lines = new List<string>();
            await using var reader = await explain.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add(reader.GetString(0));
            }

            return string.Join('\n', lines);
        }

        private static async Task ExecuteAsync(string sql, NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
        {
            await using var set = new NpgsqlCommand(sql, connection, transaction);
            await set.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
