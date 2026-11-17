namespace FilmSearch.SeedBuilder;

internal sealed class SeedBuilderOptions
{
    /// <summary>Movies taken from <c>/movie/top_rated</c> (the classics).</summary>
    public int TopRated { get; set; } = 800;

    /// <summary>Movies taken from <c>/movie/popular</c> that are not already in the top rated set (recent hits).</summary>
    public int Popular { get; set; } = 200;

    /// <summary>Popular movies with fewer votes are skipped (unreleased or obscure titles).</summary>
    public int MinPopularVotes { get; set; } = 300;

    /// <summary>Output file; default <c>data/seed/movies.seed.json</c> in the repository.</summary>
    public string? Output { get; set; }

    /// <summary>Delete the response cache before building.</summary>
    public bool Refresh { get; set; }

    public TmdbOptions Tmdb { get; } = new();
}

internal sealed class TmdbOptions
{
    /// <summary>A v3 API key (sent as <c>api_key</c>) or a v4 read access token (sent as a bearer token).</summary>
    public string? ApiKey { get; set; }

    public Uri BaseUrl { get; set; } = new("https://api.themoviedb.org/3/");

    /// <summary>Minimum delay between request starts: 250 ms ≈ 40 requests per 10 seconds (spec 08 §3).</summary>
    public TimeSpan MinRequestInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public int MaxConcurrency { get; set; } = 4;
}

internal static class RepositoryRoot
{
    public static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FilmSearch.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (FilmSearch.slnx) not found. Run the tool from the repository.");
    }
}
