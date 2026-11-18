using System.Text.Json;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Ingestion;
using FilmSearch.Core.Localization;
using FilmSearch.Infrastructure.Tmdb;

namespace FilmSearch.SeedBuilder;

internal sealed class SeedBuilder(TmdbClient tmdb, SeedBuilderOptions options, TimeProvider time)
{
    /// <summary>TMDB <c>language</c> parameter per locale.</summary>
    private static readonly Dictionary<string, string> TmdbLanguage = new(StringComparer.Ordinal)
    {
        [Locales.English] = "en-US",
        [Locales.Russian] = "ru-RU",
        [Locales.Swedish] = "sv-SE",
    };

    public async Task<SeedFile> BuildAsync(CancellationToken cancellationToken)
    {
        var today = time.GetUtcNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var genres = await LoadGenresAsync(cancellationToken);
        Console.WriteLine($"Genres: {genres.Count}");

        var ids = new List<int>();
        var seen = new HashSet<int>();
        await CollectAsync("top_rated", options.TopRated, minVotes: 0, ids, seen, today, cancellationToken);
        await CollectAsync("popular", options.Popular, options.MinPopularVotes, ids, seen, today, cancellationToken);
        Console.WriteLine($"Movie ids: {ids.Count}");

        var movies = new List<MovieImport>(ids.Count);
        var done = 0;
        await Parallel.ForEachAsync(ids, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (id, ct) =>
        {
            var details = await tmdb.GetAsync<TmdbMovieDetails>(
                $"movie/{id}?language=en-US&append_to_response=credits,keywords,translations,external_ids",
                cacheKey: $"movie-{id}",
                ct);

            if (!details.Adult && !string.IsNullOrWhiteSpace(details.Title))
            {
                var import = TmdbMovieMapper.ToImport(details);
                lock (movies)
                {
                    movies.Add(import);
                }
            }

            var count = Interlocked.Increment(ref done);
            if (count % 50 == 0 || count == ids.Count)
            {
                Console.WriteLine($"Details: {count}/{ids.Count} (requests: {tmdb.Requests}, cache hits: {tmdb.CacheHits})");
            }
        });

        var knownGenres = genres.Select(g => g.Id).ToHashSet();
        return new SeedFile
        {
            Source = "tmdb",
            GeneratedAt = time.GetUtcNow(),
            Genres = [.. genres.OrderBy(g => g.Id)],
            Movies =
            [
                .. movies
                    .Select(m => m with { GenreIds = [.. m.GenreIds.Where(knownGenres.Contains)] })
                    .OrderBy(m => m.TmdbId),
            ],
        };
    }

    public static async Task WriteAsync(SeedFile seed, string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, seed, SeedJson.Options, cancellationToken);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
        }

        File.Move(temp, path, overwrite: true);
    }

    private async Task<IReadOnlyList<GenreImport>> LoadGenresAsync(CancellationToken cancellationToken)
    {
        var lists = new Dictionary<string, TmdbGenreList>(StringComparer.Ordinal);
        foreach (var (locale, language) in TmdbLanguage)
        {
            lists[locale] = await tmdb.GetAsync<TmdbGenreList>($"genre/movie/list?language={language}", $"genres-{locale}", cancellationToken);
        }

        return TmdbMovieMapper.ToGenres(lists);
    }

    private async Task CollectAsync(
        string list,
        int count,
        int minVotes,
        List<int> ids,
        HashSet<int> seen,
        string today,
        CancellationToken cancellationToken)
    {
        var added = 0;
        for (var page = 1; added < count; page++)
        {
            // List pages change daily: the cache key includes the date.
            var result = await tmdb.GetAsync<TmdbPage<TmdbListItem>>(
                $"movie/{list}?language=en-US&page={page}",
                $"{list}-{today}-p{page}",
                cancellationToken);

            foreach (var item in result.Results)
            {
                if (added >= count)
                {
                    break;
                }

                if (!item.Adult && item.VoteCount >= minVotes && seen.Add(item.Id))
                {
                    ids.Add(item.Id);
                    added++;
                }
            }

            if (page >= result.TotalPages || page >= 500)
            {
                break;
            }
        }

        Console.WriteLine($"{list}: {added} movies");
    }
}
