using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Ingestion;
using FilmSearch.Core.Localization;

namespace FilmSearch.Evals.Datasets;

/// <summary>A loaded JSONL dataset: the cases, the file's version (short SHA-256 of its bytes) and its path.</summary>
public sealed record Dataset<T>(string Path, string Version, IReadOnlyList<T> Cases);

/// <summary>Reads and validates the JSONL datasets of <c>evals/datasets</c> (spec 09 §2). Validation also runs in CI (unit tests).</summary>
public static class DatasetLoader
{
    public const string SearchFileName = "search.golden.jsonl";
    public const string QuFileName = "qu.golden.jsonl";
    public const string AssistantScenariosFileName = "assistant.scenarios.jsonl";
    public const string AssistantAdversarialFileName = "assistant.adversarial.jsonl";

    /// <summary>Spec 09 §2: 25–40 scenarios, 15–25 adversarial cases; every locale is covered.</summary>
    public const int MinScenarioCases = 25;
    public const int MinAdversarialCases = 15;
    public const int MinAssistantCasesPerLocale = 4;

    /// <summary>Spec 09 §2: at least this many search queries per language.</summary>
    public const int MinSearchCasesPerLocale = 25;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // A misspelled field ("relevent") is an error, not a silently empty list.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static Dataset<T> Load<T>(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var version = Convert.ToHexStringLower(SHA256.HashData(bytes))[..12];
        var cases = new List<T>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                cases.Add(JsonSerializer.Deserialize<T>(line, Json) ?? throw new JsonException("The line is null."));
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{System.IO.Path.GetFileName(path)}:{lineNumber}: {ex.Message}", ex);
            }
        }

        return new Dataset<T>(path, version, cases);
    }

    /// <summary>Movie TMDB ids and genre slugs of the seed file: what dataset cases may refer to.</summary>
    public static SeedCatalog LoadSeed(string seedPath)
    {
        using var stream = File.OpenRead(seedPath);
        var seed = JsonSerializer.Deserialize<SeedFile>(stream, SeedJson.Options)
            ?? throw new InvalidDataException($"Seed file {seedPath} is empty.");
        var slugs = seed.Genres.ToDictionary(g => g.Id, g => g.Slug);

        return new SeedCatalog(
            seed.Movies.ToDictionary(m => m.TmdbId, m => (IReadOnlySet<string>)m.GenreIds.Select(id => slugs[id]).ToHashSet(StringComparer.Ordinal)),
            slugs.Values.ToHashSet(StringComparer.Ordinal))
        {
            Titles = seed.Movies.ToDictionary(
                m => m.TmdbId,
                m => (m.Translations.TryGetValue(Locales.English, out var en) ? en.Title : null) ?? m.OriginalTitle),
            GeneratedAt = seed.GeneratedAt,
        };
    }

    /// <summary>Problems of the search dataset, one line each; empty when it is valid.</summary>
    public static IReadOnlyList<string> ValidateSearch(IReadOnlyList<SearchCase> cases, SeedCatalog seed)
    {
        var errors = new List<string>();
        foreach (var duplicate in cases.GroupBy(c => c.Id).Where(g => g.Count() > 1))
        {
            errors.Add($"{duplicate.Key}: duplicate id.");
        }

        foreach (var c in cases)
        {
            void Error(string message) => errors.Add($"{c.Id}: {message}");

            if (string.IsNullOrWhiteSpace(c.Id))
            {
                errors.Add("A case has no id.");
            }

            if (string.IsNullOrWhiteSpace(c.Query))
            {
                Error("query is empty.");
            }

            if (!Locales.All.Contains(c.Locale))
            {
                Error($"locale '{c.Locale}' is not one of {string.Join(", ", Locales.All)}.");
            }

            if (c.Relevant.Count == 0)
            {
                Error("relevant is empty.");
            }

            if (c.Relevant.Distinct().Count() != c.Relevant.Count)
            {
                Error("relevant has duplicates.");
            }

            foreach (var id in c.Relevant.Where(id => !seed.Movies.ContainsKey(id)))
            {
                Error($"relevant movie {id} is not in the seed.");
            }

            foreach (var (key, grade) in c.Graded ?? new Dictionary<string, int>())
            {
                if (!int.TryParse(key, out var id) || !c.Relevant.Contains(id))
                {
                    Error($"graded movie {key} is not in relevant.");
                }

                if (grade is < 1 or > 3)
                {
                    Error($"grade {grade} of movie {key} is not 1–3.");
                }
            }

            if (c.Tags.Count == 0)
            {
                Error("tags are empty.");
            }

            foreach (var tag in c.Tags.Where(t => !SearchTags.All.Contains(t)))
            {
                Error($"unknown tag '{tag}'.");
            }

            foreach (var genre in c.ExcludedGenres ?? [])
            {
                if (!seed.GenreSlugs.Contains(genre))
                {
                    Error($"excluded genre '{genre}' is not a genre slug of the seed.");
                }

                foreach (var id in c.Relevant.Where(id => seed.Movies.TryGetValue(id, out var genres) && genres.Contains(genre)))
                {
                    Error($"relevant movie {id} has the excluded genre '{genre}'.");
                }
            }
        }

        foreach (var locale in Locales.All)
        {
            var count = cases.Count(c => c.Locale == locale);
            if (count < MinSearchCasesPerLocale)
            {
                errors.Add($"Only {count} cases in '{locale}'; at least {MinSearchCasesPerLocale} are needed (spec 09 §2).");
            }
        }

        return errors;
    }

    /// <summary>Problems of the query understanding dataset, one line each; empty when it is valid.</summary>
    public static IReadOnlyList<string> ValidateQu(IReadOnlyList<QuCase> cases, SeedCatalog seed)
    {
        var errors = new List<string>();
        foreach (var duplicate in cases.GroupBy(c => c.Id).Where(g => g.Count() > 1))
        {
            errors.Add($"{duplicate.Key}: duplicate id.");
        }

        foreach (var c in cases)
        {
            void Error(string message) => errors.Add($"{c.Id}: {message}");

            if (string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Query))
            {
                Error("id and query are required.");
            }

            if (!Locales.All.Contains(c.Locale))
            {
                Error($"locale '{c.Locale}' is not one of {string.Join(", ", Locales.All)}.");
            }

            var e = c.Expected;
            foreach (var genre in e.GenresInclude.Concat(e.GenresExclude).Where(g => !seed.GenreSlugs.Contains(g)))
            {
                Error($"genre '{genre}' is not a genre slug of the seed.");
            }

            if (e.GenresInclude.Intersect(e.GenresExclude).Any())
            {
                Error("a genre is both included and excluded.");
            }

            if (e.YearFrom is < 1880 or > 2100 || e.YearTo is < 1880 or > 2100)
            {
                Error("years must be between 1880 and 2100.");
            }

            if (e.YearFrom > e.YearTo)
            {
                Error("yearFrom is after yearTo.");
            }

            if (e.MinRating is < 0 or > 10)
            {
                Error("minRating must be 0–10.");
            }

            if (e.MaxPriceMinor is <= 0)
            {
                Error("maxPriceMinor must be positive.");
            }

            if (e.Language is { } language && (language.Length != 2 || !language.All(char.IsAsciiLetterLower)))
            {
                Error($"language '{language}' is not an ISO 639-1 code.");
            }

            if (c.Tags.Count == 0)
            {
                Error("tags are empty.");
            }
        }

        return errors;
    }

    /// <summary>Problems of an assistant dataset (scenarios or adversarial), one line each; empty when it is valid.</summary>
    public static IReadOnlyList<string> ValidateAssistant(IReadOnlyList<AssistantCase> cases, SeedCatalog seed, int minCases)
    {
        var errors = new List<string>();
        foreach (var duplicate in cases.GroupBy(c => c.Id).Where(g => g.Count() > 1))
        {
            errors.Add($"{duplicate.Key}: duplicate id.");
        }

        foreach (var c in cases)
        {
            void Error(string message) => errors.Add($"{c.Id}: {message}");

            if (string.IsNullOrWhiteSpace(c.Id))
            {
                errors.Add("A case has no id.");
            }

            if (!Locales.All.Contains(c.Locale))
            {
                Error($"locale '{c.Locale}' is not one of {string.Join(", ", Locales.All)}.");
            }

            if (c.Tags.Count == 0)
            {
                Error("tags are empty.");
            }

            foreach (var tag in c.Tags.Where(t => !AssistantTags.All.Contains(t)))
            {
                Error($"unknown tag '{tag}'.");
            }

            if (c.Turns.Count == 0)
            {
                Error("turns are empty.");
            }

            foreach (var line in c.Setup?.Cart ?? [])
            {
                if (!seed.Movies.ContainsKey(line.TmdbId))
                {
                    Error($"setup cart movie {line.TmdbId} is not in the seed.");
                }

                if (line.Offer is not ("buy" or "rent"))
                {
                    Error($"setup cart offer '{line.Offer}' is not buy or rent.");
                }
            }

            if (c.Setup?.StoppedMessages?.Any(m => string.IsNullOrWhiteSpace(m) || m.Length > AssistantLimits.MaxMessageLength) == true)
            {
                Error($"setup stopped messages must be 1–{AssistantLimits.MaxMessageLength} characters.");
            }

            for (var i = 0; i < c.Turns.Count; i++)
            {
                var turn = c.Turns[i];
                var number = i + 1;
                void TurnError(string message) => Error($"turn {number}: {message}");

                if (string.IsNullOrWhiteSpace(turn.User) || turn.User.Length > AssistantLimits.MaxMessageLength)
                {
                    TurnError($"user text must be 1–{AssistantLimits.MaxMessageLength} characters (the API limit).");
                }

                if (turn.Expect is not { } e)
                {
                    continue;
                }

                foreach (var tool in e.ToolsCalled.Concat(e.ToolsNotCalled).Where(t => t != AssistantToolNames.Any && !AssistantToolNames.All.Contains(t)))
                {
                    TurnError($"unknown tool '{tool}'.");
                }

                if (e.ToolsCalled.Intersect(e.ToolsNotCalled).Any() || (e.ToolsCalled.Count > 0 && e.ToolsNotCalled.Contains(AssistantToolNames.Any)))
                {
                    TurnError("a tool is both required and forbidden.");
                }

                var movies = e.MoviesAnyOf.Concat(e.Cart?.Contains ?? []).Concat(e.Cart?.NotContains ?? []);
                foreach (var id in movies.Where(id => !seed.Movies.ContainsKey(id)))
                {
                    TurnError($"movie {id} is not in the seed.");
                }

                foreach (var genre in e.CardsExcludeGenres.Where(g => !seed.GenreSlugs.Contains(g)))
                {
                    TurnError($"genre '{genre}' is not a genre slug of the seed.");
                }

                if (e.MinCards > e.MaxCards)
                {
                    TurnError("minCards is greater than maxCards.");
                }

                if (e.CardsYearFrom > e.CardsYearTo)
                {
                    TurnError("cardsYearFrom is after cardsYearTo.");
                }

                if (e.Cart?.Offer is { } offer && offer is not ("buy" or "rent"))
                {
                    TurnError($"cart offer '{offer}' is not buy or rent.");
                }

                if (e.Language is { } language && language != "any" && !Locales.All.Contains(language))
                {
                    TurnError($"language '{language}' is not one of {string.Join(", ", Locales.All)} or any.");
                }

                foreach (var pattern in e.AnswerMatches.Concat(e.AnswerNotMatches))
                {
                    try
                    {
                        _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    }
                    catch (ArgumentException ex)
                    {
                        TurnError($"pattern '{pattern}' is not a valid regular expression: {ex.Message}");
                    }
                }
            }
        }

        if (cases.Count < minCases)
        {
            errors.Add($"Only {cases.Count} cases; at least {minCases} are needed (spec 09 §2).");
        }

        foreach (var locale in Locales.All)
        {
            var count = cases.Count(c => c.Locale == locale);
            if (count < MinAssistantCasesPerLocale)
            {
                errors.Add($"Only {count} cases in '{locale}'; at least {MinAssistantCasesPerLocale} are needed.");
            }
        }

        return errors;
    }
}

/// <summary>What datasets may refer to: genre slugs of each seed movie (by TMDB id) and all genre slugs.</summary>
public sealed record SeedCatalog(IReadOnlyDictionary<int, IReadOnlySet<string>> Movies, IReadOnlySet<string> GenreSlugs)
{
    /// <summary>English title by TMDB id, for reports.</summary>
    public IReadOnlyDictionary<int, string> Titles { get; init; } = new Dictionary<int, string>();

    public DateTimeOffset GeneratedAt { get; init; }

    public string TitleOf(int tmdbId) => Titles.GetValueOrDefault(tmdbId) ?? $"tmdb:{tmdbId}";
}
