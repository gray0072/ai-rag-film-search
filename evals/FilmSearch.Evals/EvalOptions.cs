using System.Globalization;

namespace FilmSearch.Evals;

/// <summary>Command line of the runner (spec 09 §1). <see cref="Parse"/> throws <see cref="ArgumentException"/> with a message for the user.</summary>
public sealed record EvalOptions
{
    public const string SearchSuite = "search";
    public const string QuSuite = "qu";
    public const string ValidateSuite = "validate";
    public const string AssistantSuite = "assistant";
    public const string AdversarialSuite = "adversarial";

    public const string Usage = """
        FilmSearch evaluation runner (spec 09). Runs a suite over a golden dataset and writes
        evals/reports/YYYY-MM-DD_<suite>_<label>.md and .json.

          dotnet run --project evals/FilmSearch.Evals -- --suite search --label baseline
          dotnet run --project evals/FilmSearch.Evals -- --suite search --label rrf-k20 --modes hybrid --set Search:Hybrid:RrfK=20
          dotnet run --project evals/FilmSearch.Evals -- --suite qu --label baseline --repeat 3
          dotnet run --project evals/FilmSearch.Evals -- --suite assistant --label gemma-v2 --judge-profile openrouter --judge-model openai/gpt-6-luna
          dotnet run --project evals/FilmSearch.Evals -- --suite adversarial --label gemma-v2 --no-judge
          dotnet run --project evals/FilmSearch.Evals -- --suite validate

        Options:
          --suite <name>               search: retrieval metrics per mode; qu: query understanding accuracy;
                                       assistant: scenario conversations through the agent (checks + LLM judge);
                                       adversarial: the same over the adversarial set;
                                       validate: check the datasets against the seed file (no DB, no models)
          --label <name>               report name, e.g. baseline, rrf-k20 (required except for validate)
          --modes <list>               search modes, comma-separated: keyword, semantic, hybrid, hybrid+qu
                                       (default: all four)
          --profile <name>             embedding profile (AI:ActiveEmbeddingProfile), default from appsettings
          --chat-profile <name>        chat profile of query understanding and the assistant (AI:ActiveChatProfile)
          --repeat <n>                 runs per case; metrics are averaged (default: 1 for search, 3 otherwise)
          --judge-profile <name>       assistant suites: chat profile of the LLM judge (default: the chat profile)
          --judge-model <id>           assistant suites: the judge profile with another model, e.g. openai/gpt-6-luna
          --no-judge                   assistant suites: deterministic checks only
          --cases <list>               assistant suites: case ids, comma-separated; a trailing * matches a prefix
          --parallel <n>               assistant suites: case runs at the same time (default 1; LM Studio runs one
                                       request at a time, cloud providers take more)
          --set <key>=<value>          configuration override, repeatable, e.g. Search:Hybrid:KeywordWeight=0.5
          --connection <string>        use this PostgreSQL database instead of a fresh container (the seed is loaded
                                       when the catalog is empty, stale vectors are embedded)
          --dataset <path>             dataset file (default: evals/datasets/<suite>.golden.jsonl)
          --out <dir>                  report folder (default: evals/reports)

        Needs Docker (fresh pgvector container per run) and the AI providers of the profiles (LM Studio by default).
        """;

    public required string Suite { get; init; }

    public string Label { get; init; } = "";

    public IReadOnlyList<SearchModeOption> Modes { get; init; } = SearchModeOption.All;

    public string? EmbeddingProfile { get; init; }

    public string? ChatProfile { get; init; }

    public int Repeat { get; init; } = 1;

    public IReadOnlyDictionary<string, string> Overrides { get; init; } = new Dictionary<string, string>();

    public string? Connection { get; init; }

    public string? DatasetPath { get; init; }

    public string? OutputDirectory { get; init; }

    public string? JudgeProfile { get; init; }

    public string? JudgeModel { get; init; }

    public bool NoJudge { get; init; }

    /// <summary>Case id filters (exact, or a prefix with a trailing <c>*</c>); empty = all cases.</summary>
    public IReadOnlyList<string> Cases { get; init; } = [];

    public int Parallel { get; init; } = 1;

    public bool IsAssistantSuite => Suite is AssistantSuite or AdversarialSuite;

    /// <summary>True when <paramref name="id"/> passes <see cref="Cases"/>.</summary>
    public bool IncludesCase(string id) =>
        Cases.Count == 0 || Cases.Any(f => f.EndsWith('*') ? id.StartsWith(f[..^1], StringComparison.Ordinal) : id == f);

    public static EvalOptions Parse(IReadOnlyList<string> args)
    {
        string? suite = null, label = null, modes = null, embedding = null, chat = null, connection = null, dataset = null, output = null;
        string? judgeProfile = null, judgeModel = null, cases = null;
        var noJudge = false;
        int? repeat = null;
        var parallel = 1;
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{name} needs a value.");

            switch (name)
            {
                case "--suite": suite = Value(); break;
                case "--label": label = Value(); break;
                case "--modes": modes = Value(); break;
                case "--profile": embedding = Value(); break;
                case "--chat-profile": chat = Value(); break;
                case "--connection": connection = Value(); break;
                case "--dataset": dataset = Value(); break;
                case "--out": output = Value(); break;
                case "--judge-profile": judgeProfile = Value(); break;
                case "--judge-model": judgeModel = Value(); break;
                case "--no-judge": noJudge = true; break;
                case "--cases": cases = Value(); break;
                case "--parallel":
                    parallel = int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p is >= 1 and <= 16
                        ? p
                        : throw new ArgumentException("--parallel must be a number from 1 to 16.");
                    break;
                case "--repeat":
                    repeat = int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 20
                        ? n
                        : throw new ArgumentException("--repeat must be a number from 1 to 20.");
                    break;
                case "--set":
                    var pair = Value();
                    var eq = pair.IndexOf('=', StringComparison.Ordinal);
                    if (eq <= 0)
                    {
                        throw new ArgumentException($"--set expects key=value, got '{pair}'.");
                    }

                    overrides[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{name}'.");
            }
        }

        if (suite is not (SearchSuite or QuSuite or AssistantSuite or AdversarialSuite or ValidateSuite))
        {
            throw new ArgumentException("--suite must be search, qu, assistant, adversarial or validate.");
        }

        if (suite != ValidateSuite && string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("--label is required.");
        }

        if (label is not null && label.IndexOfAny([.. Path.GetInvalidFileNameChars(), ' ']) >= 0)
        {
            throw new ArgumentException("--label must be usable in a file name (no spaces).");
        }

        return new EvalOptions
        {
            Suite = suite,
            Label = label ?? "",
            Modes = modes is null ? SearchModeOption.All : SearchModeOption.ParseList(modes),
            EmbeddingProfile = embedding,
            ChatProfile = chat,
            Repeat = repeat ?? (suite == SearchSuite ? 1 : 3),
            Overrides = overrides,
            Connection = connection,
            DatasetPath = dataset,
            OutputDirectory = output,
            JudgeProfile = judgeProfile,
            JudgeModel = judgeModel,
            NoJudge = noJudge,
            Cases = cases is null ? [] : [.. cases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            Parallel = parallel,
        };
    }
}
