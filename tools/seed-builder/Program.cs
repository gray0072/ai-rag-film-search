using FilmSearch.SeedBuilder;
using Microsoft.Extensions.Configuration;

// Builds data/seed/movies.seed.json from TMDB (spec 08 §2).
//
//   dotnet user-secrets set Tmdb:ApiKey <v3 key> --project tools/seed-builder
//   dotnet run --project tools/seed-builder [-- --TopRated 800 --Popular 200 --Refresh true]
//
// Responses are cached in tools/seed-builder/.cache, so a rerun resumes where it stopped.

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var options = new SeedBuilderOptions();
configuration.Bind(options);
configuration.GetSection("Tmdb").Bind(options.Tmdb);

if (string.IsNullOrWhiteSpace(options.Tmdb.ApiKey))
{
    await Console.Error.WriteLineAsync(
        "TMDB API key is missing. Set it with: dotnet user-secrets set Tmdb:ApiKey <key> --project tools/seed-builder "
        + "(a v3 API key or a v4 read access token; or the TMDB__APIKEY environment variable).");
    return 1;
}

var repositoryRoot = RepositoryRoot.Find();
var output = options.Output ?? Path.Combine(repositoryRoot, "data", "seed", "movies.seed.json");
var cacheDirectory = Path.Combine(repositoryRoot, "tools", "seed-builder", ".cache");
if (options.Refresh && Directory.Exists(cacheDirectory))
{
    Directory.Delete(cacheDirectory, recursive: true);
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var tmdb = new TmdbClient(options.Tmdb, cacheDirectory);
var builder = new SeedBuilder(tmdb, options, TimeProvider.System);
var seed = await builder.BuildAsync(cts.Token);
await SeedBuilder.WriteAsync(seed, output, cts.Token);

Console.WriteLine($"Wrote {seed.Movies.Count} movies and {seed.Genres.Count} genres to {output}");
return 0;
