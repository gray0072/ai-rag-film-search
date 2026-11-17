using System.Diagnostics;
using System.Globalization;
using System.Text;
using FilmSearch.AI.Providers;
using FilmSearch.Core.Search;
using FilmSearch.Infrastructure.Search.Hybrid;
using FilmSearch.Infrastructure.Search.Semantic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FilmSearch.Evals.Reports;

/// <summary>
/// What a result depends on (spec 09 §1): code version, dataset version, models, prompt and search parameters.
/// Two reports are comparable when their headers differ only in the parameter under test.
/// </summary>
public sealed record RunHeader
{
    public required string Suite { get; init; }

    public required string Label { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required string CommandLine { get; init; }

    /// <summary>Short commit hash, with <c>+dirty</c> when the working tree had changes.</summary>
    public required string Commit { get; init; }

    public required string Dataset { get; init; }

    public required string DatasetVersion { get; init; }

    public required int Cases { get; init; }

    public required int Repeat { get; init; }

    public required string Database { get; init; }

    public required string SeedGeneratedAt { get; init; }

    public string? EmbeddingProfile { get; init; }

    public string? EmbeddingModel { get; init; }

    public string? EmbeddingText { get; init; }

    public IReadOnlyList<string> EmbeddingLocales { get; init; } = [];

    public string? ChatProfile { get; init; }

    public string? ChatModel { get; init; }

    /// <summary>Prompt versions seen in query understanding results, e.g. <c>query-understanding.v1</c>.</summary>
    public IReadOnlyList<string> Prompts { get; set; } = [];

    /// <summary>Search parameters in effect (semantic and hybrid options).</summary>
    public IReadOnlyDictionary<string, string> SearchParameters { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Overrides { get; init; } = new Dictionary<string, string>();

    public static RunHeader Create(
        EvalOptions options,
        EvalEnvironment environment,
        string repositoryRoot,
        string datasetPath,
        string datasetVersion,
        int cases,
        DateTimeOffset seedGeneratedAt,
        bool usesEmbeddings,
        bool usesChat)
    {
        var services = environment.Services;
        var ai = services.GetRequiredService<IOptions<AiOptions>>().Value;
        var embedding = ai.GetEmbeddingProfile(ai.ActiveEmbeddingProfile);
        var chat = ai.GetChatProfile(ai.ActiveChatProfile);
        var semantic = services.GetRequiredService<IOptions<SemanticSearchOptions>>().Value;
        var hybrid = services.GetRequiredService<IOptions<HybridSearchOptions>>().Value;
        var index = services.GetRequiredService<IOptions<EmbeddingIndexOptions>>().Value;

        var parameters = new Dictionary<string, string>
        {
            ["Search:Semantic:MaxResults"] = Format(semantic.MaxResults),
            ["Search:Hybrid:RrfK"] = Format(hybrid.RrfK),
            ["Search:Hybrid:KeywordWeight"] = Format(hybrid.KeywordWeight),
            ["Search:Hybrid:RelaxedKeywordWeight"] = Format(hybrid.RelaxedKeywordWeight),
            ["Search:Hybrid:SemanticWeight"] = Format(hybrid.SemanticWeight),
            ["Search:Hybrid:CandidatesPerList"] = Format(hybrid.CandidatesPerList),
            ["Search:Hybrid:SimilarTitles"] = hybrid.SimilarTitles.ToString(),
        };

        return new RunHeader
        {
            Suite = options.Suite,
            Label = options.Label,
            StartedAt = DateTimeOffset.UtcNow,
            CommandLine = string.Join(' ', Environment.GetCommandLineArgs().Skip(1)),
            Commit = GitCommit(repositoryRoot),
            Dataset = Path.GetRelativePath(repositoryRoot, datasetPath).Replace('\\', '/'),
            DatasetVersion = datasetVersion,
            Cases = cases,
            Repeat = options.Repeat,
            Database = environment.FreshDatabase ? $"fresh container {EvalEnvironment.PostgresImage}" : "existing database (--connection)",
            SeedGeneratedAt = seedGeneratedAt.ToString("u", CultureInfo.InvariantCulture),
            EmbeddingProfile = usesEmbeddings ? ai.ActiveEmbeddingProfile : null,
            EmbeddingModel = usesEmbeddings
                ? $"{embedding.Model} ({Format(embedding.Dimensions)} dims, query prefix \"{embedding.QueryPrefix}\", document prefix \"{embedding.DocumentPrefix}\")"
                : null,
            EmbeddingText = usesEmbeddings ? MovieEmbeddingText.Version : null,
            EmbeddingLocales = usesEmbeddings ? index.ResolveLocales() : [],
            ChatProfile = usesChat ? ai.ActiveChatProfile : null,
            ChatModel = usesChat
                ? $"{chat.Model} (reasoning {chat.ReasoningEffort?.ToString() ?? "default"})"
                : null,
            SearchParameters = usesEmbeddings ? parameters : new Dictionary<string, string>(),
            Overrides = options.Overrides,
        };
    }

    public string ToMarkdown()
    {
        var md = new StringBuilder();
        md.AppendLine("| | |");
        md.AppendLine("|---|---|");
        Row("Suite / label", $"`{Suite}` / `{Label}`");
        Row("Started (UTC)", StartedAt.ToString("u", CultureInfo.InvariantCulture));
        Row("Commit", $"`{Commit}`");
        Row("Command", $"`{CommandLine}`");
        Row("Dataset", $"`{Dataset}` version `{DatasetVersion}`, {Format(Cases)} cases, repeat {Format(Repeat)}");
        Row("Database", $"{Database}, seed generated {SeedGeneratedAt}");
        if (EmbeddingModel is not null)
        {
            Row("Embeddings", $"profile `{EmbeddingProfile}`: {EmbeddingModel}; text `{EmbeddingText}`; locales {string.Join(", ", EmbeddingLocales)}");
        }

        if (ChatModel is not null)
        {
            Row("Chat", $"profile `{ChatProfile}`: {ChatModel}");
        }

        if (Prompts.Count > 0)
        {
            Row("Prompts", string.Join(", ", Prompts.Select(p => $"`{p}`")));
        }

        if (SearchParameters.Count > 0)
        {
            Row("Search parameters", string.Join(", ", SearchParameters.Select(p => $"`{p.Key.Replace("Search:", "", StringComparison.Ordinal)}={p.Value}`")));
        }

        if (Overrides.Count > 0)
        {
            Row("Overrides", string.Join(", ", Overrides.Select(p => $"`{p.Key}={p.Value}`")));
        }

        return md.ToString();

        void Row(string name, string value) => md.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {value.Replace("|", "\\|", StringComparison.Ordinal)} |");
    }

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string GitCommit(string repositoryRoot)
    {
        var hash = Git(repositoryRoot, "rev-parse --short HEAD") ?? "unknown";
        var status = Git(repositoryRoot, "status --porcelain");
        return string.IsNullOrWhiteSpace(status) ? hash : hash + "+dirty";
    }

    private static string? Git(string workingDirectory, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
