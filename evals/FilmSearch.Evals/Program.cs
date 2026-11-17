using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FilmSearch.Evals.Datasets;
using FilmSearch.Evals.Reports;
using FilmSearch.Evals.Suites;

namespace FilmSearch.Evals;

/// <summary>Entry point: <c>dotnet run --project evals/FilmSearch.Evals -- --suite search --label baseline</c>. See <see cref="EvalOptions.Usage"/>.</summary>
internal static class Program
{
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(EvalOptions.Usage);
            return 0;
        }

        EvalOptions options;
        try
        {
            options = EvalOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message + Environment.NewLine + Environment.NewLine + EvalOptions.Usage);
            return 2;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var root = RepositoryPaths.FindRoot();
        var seed = DatasetLoader.LoadSeed(RepositoryPaths.SeedFile(root));

        return options.Suite switch
        {
            EvalOptions.ValidateSuite => Validate(root, seed),
            EvalOptions.QuSuite => await RunQuAsync(options, root, seed, cts.Token),
            _ => await RunSearchAsync(options, root, seed, cts.Token),
        };
    }

    private static int Validate(string root, SeedCatalog seed)
    {
        var search = DatasetLoader.Load<SearchCase>(Path.Combine(RepositoryPaths.Datasets(root), DatasetLoader.SearchFileName));
        var qu = DatasetLoader.Load<QuCase>(Path.Combine(RepositoryPaths.Datasets(root), DatasetLoader.QuFileName));
        var errors = DatasetLoader.ValidateSearch(search.Cases, seed).Concat(DatasetLoader.ValidateQu(qu.Cases, seed)).ToList();
        foreach (var error in errors)
        {
            Console.Error.WriteLine(error);
        }

        Console.WriteLine($"{DatasetLoader.SearchFileName}: {search.Cases.Count} cases, version {search.Version}");
        Console.WriteLine($"{DatasetLoader.QuFileName}: {qu.Cases.Count} cases, version {qu.Version}");
        Console.WriteLine(errors.Count == 0 ? "Datasets are valid." : $"{errors.Count} problem(s).");
        return errors.Count == 0 ? 0 : 1;
    }

    private static async Task<int> RunSearchAsync(EvalOptions options, string root, SeedCatalog seed, CancellationToken ct)
    {
        var dataset = LoadValid<SearchCase>(options, root, DatasetLoader.SearchFileName, cases => DatasetLoader.ValidateSearch(cases, seed));
        if (dataset is null)
        {
            return 1;
        }

        await using var environment = await EvalEnvironment.StartAsync(options, root, ct);
        await environment.PrepareCatalogAsync(ct);
        await environment.PrepareEmbeddingsAsync(ct);
        var tmdbIds = await environment.GetTmdbIdsAsync(ct);

        var header = RunHeader.Create(
            options, environment, root, dataset.Path, dataset.Version, dataset.Cases.Count, seed.GeneratedAt,
            usesEmbeddings: true,
            usesChat: options.Modes.Any(m => m.Understand));
        var results = await new SearchSuite(environment.Services, tmdbIds).RunAsync(dataset.Cases, options.Modes, options.Repeat, ct);
        header.Prompts = [.. results.SelectMany(r => r.Runs).Select(r => r.Understood?.PromptVersion).OfType<string>().Distinct().Order()];

        var modes = options.Modes.Select(m => m.Name).ToList();
        await WriteReportAsync(options, root, header, SearchReport.ToMarkdown(header, results, modes, seed), SearchReport.ToJson(header, results, modes), ct);
        return 0;
    }

    private static async Task<int> RunQuAsync(EvalOptions options, string root, SeedCatalog seed, CancellationToken ct)
    {
        var dataset = LoadValid<QuCase>(options, root, DatasetLoader.QuFileName, cases => DatasetLoader.ValidateQu(cases, seed));
        if (dataset is null)
        {
            return 1;
        }

        // Query understanding reads the genre names from the catalog: the seed is needed, embeddings are not.
        await using var environment = await EvalEnvironment.StartAsync(options, root, ct);
        await environment.PrepareCatalogAsync(ct);

        var header = RunHeader.Create(
            options, environment, root, dataset.Path, dataset.Version, dataset.Cases.Count, seed.GeneratedAt,
            usesEmbeddings: false,
            usesChat: true);
        var results = await new QuSuite(environment.Services).RunAsync(dataset.Cases, options.Repeat, ct);
        header.Prompts = [.. results.SelectMany(r => r.Runs).Select(r => r.Understood.PromptVersion).OfType<string>().Distinct().Order()];

        await WriteReportAsync(options, root, header, QuReport.ToMarkdown(header, results), QuReport.ToJson(header, results), ct);
        return 0;
    }

    private static Dataset<T>? LoadValid<T>(EvalOptions options, string root, string fileName, Func<IReadOnlyList<T>, IReadOnlyList<string>> validate)
    {
        var path = Path.GetFullPath(options.DatasetPath ?? Path.Combine(RepositoryPaths.Datasets(root), fileName));
        var dataset = DatasetLoader.Load<T>(path);
        var errors = validate(dataset.Cases);
        foreach (var error in errors)
        {
            Console.Error.WriteLine(error);
        }

        return errors.Count == 0 ? dataset : null;
    }

    private static async Task WriteReportAsync(EvalOptions options, string root, RunHeader header, string markdown, object json, CancellationToken ct)
    {
        var directory = Path.GetFullPath(options.OutputDirectory ?? Path.Combine(root, "evals", "reports"));
        Directory.CreateDirectory(directory);
        var name = $"{header.StartedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}_{header.Suite}_{header.Label}";
        var markdownPath = Path.Combine(directory, name + ".md");
        var jsonPath = Path.Combine(directory, name + ".json");

        // LF on every OS: reports are committed.
        await File.WriteAllTextAsync(markdownPath, markdown.ReplaceLineEndings("\n"), ct);
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(json, ReportJson) + "\n", ct);

        Console.WriteLine(markdown);
        Console.Error.WriteLine($"Report: {markdownPath}");
        Console.Error.WriteLine($"Raw results: {jsonPath}");
    }
}

/// <summary>Paths inside the repository, found from the build output folder.</summary>
public static class RepositoryPaths
{
    public static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FilmSearch.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (FilmSearch.slnx) not found. Run the evaluations from the repository.");
    }

    public static string Datasets(string root) => Path.Combine(root, "evals", "datasets");

    public static string SeedFile(string root) => Path.Combine(root, "data", "seed", "movies.seed.json");
}
