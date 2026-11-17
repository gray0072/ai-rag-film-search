using FilmSearch.Core.Search;
using FilmSearch.Evals;
using FilmSearch.Evals.Datasets;
using FilmSearch.Evals.Suites;

namespace FilmSearch.UnitTests.Evals;

/// <summary>The committed golden datasets are valid against the committed seed (spec 09 §4: CI checks the datasets).</summary>
public sealed class DatasetTests
{
    private static readonly string Root = RepositoryPaths.FindRoot();
    private static readonly SeedCatalog Seed = DatasetLoader.LoadSeed(RepositoryPaths.SeedFile(Root));

    [Fact]
    public void Search_golden_set_is_valid()
    {
        var dataset = DatasetLoader.Load<SearchCase>(Path.Combine(RepositoryPaths.Datasets(Root), DatasetLoader.SearchFileName));

        DatasetLoader.ValidateSearch(dataset.Cases, Seed).ShouldBeEmpty();
        dataset.Cases.Count.ShouldBeGreaterThanOrEqualTo(90);
    }

    [Fact]
    public void Search_golden_set_covers_every_tag_in_every_locale()
    {
        var dataset = DatasetLoader.Load<SearchCase>(Path.Combine(RepositoryPaths.Datasets(Root), DatasetLoader.SearchFileName));

        foreach (var locale in new[] { "en", "ru", "sv" })
        {
            var tags = dataset.Cases.Where(c => c.Locale == locale).SelectMany(c => c.Tags).ToHashSet();
            var expected = locale == "en" ? SearchTags.All.Except([SearchTags.CrossLanguage]) : SearchTags.All;
            expected.Except(tags).ShouldBeEmpty($"locale {locale}");
        }
    }

    [Fact]
    public void Query_understanding_golden_set_is_valid()
    {
        var dataset = DatasetLoader.Load<QuCase>(Path.Combine(RepositoryPaths.Datasets(Root), DatasetLoader.QuFileName));

        DatasetLoader.ValidateQu(dataset.Cases, Seed).ShouldBeEmpty();
        dataset.Cases.Count.ShouldBeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void Validation_reports_unknown_movies_genres_tags_and_bad_grades()
    {
        var seed = new SeedCatalog(
            new Dictionary<int, IReadOnlySet<string>> { [1] = new HashSet<string> { "horror" }, [2] = new HashSet<string> { "drama" } },
            new HashSet<string> { "horror", "drama" });
        SearchCase[] cases =
        [
            new("a", "query", "en", [1, 99], ["plot", "unknown-tag"], Graded: new Dictionary<string, int> { ["1"] = 5, ["2"] = 1 }, ExcludedGenres: ["horror", "musical"]),
            new("a", "", "de", [], []),
        ];

        var errors = DatasetLoader.ValidateSearch(cases, seed);

        errors.ShouldContain("a: duplicate id.");
        errors.ShouldContain("a: relevant movie 99 is not in the seed.");
        errors.ShouldContain("a: unknown tag 'unknown-tag'.");
        errors.ShouldContain(e => e.Contains("grade 5", StringComparison.Ordinal));
        errors.ShouldContain("a: graded movie 2 is not in relevant.");
        errors.ShouldContain("a: excluded genre 'musical' is not a genre slug of the seed.");
        errors.ShouldContain("a: relevant movie 1 has the excluded genre 'horror'.");
        errors.ShouldContain("a: query is empty.");
        errors.ShouldContain(e => e.Contains("locale 'de'", StringComparison.Ordinal));
        errors.ShouldContain("a: relevant is empty.");
        errors.ShouldContain(e => e.StartsWith("Only 1 cases in 'en'", StringComparison.Ordinal));
    }

    [Fact]
    public void Loader_rejects_misspelled_and_missing_fields()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"id":"x","query":"q","locale":"en","relevent":[1],"tags":["plot"]}""");

            Should.Throw<InvalidDataException>(() => DatasetLoader.Load<SearchCase>(path)).Message.ShouldContain(":1:");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Grades_default_to_one()
    {
        var searchCase = new SearchCase("a", "q", "en", [1, 2], ["plot"], Graded: new Dictionary<string, int> { ["2"] = 3 });

        searchCase.Grades().ShouldBe(new Dictionary<int, int> { [1] = 1, [2] = 3 });
    }

    [Fact]
    public void Query_understanding_comparison_treats_lists_as_sets_and_invented_filters_as_wrong()
    {
        var expected = new QuExpected { GenresInclude = ["comedy", "drama"], YearFrom = 1990, SimilarToTitles = ["The Matrix"] };
        var actual = new UnderstoodQuery
        {
            SemanticQuery = "",
            Filters = new UnderstoodFilters { GenresInclude = ["drama", "comedy"], YearFrom = 1990, MinRating = 7 },
            SimilarToTitles = ["Matrix"],
        };

        var fields = QuSuite.Compare(expected, actual);

        fields[QuSuite.GenresInclude].ShouldBeTrue();
        fields[QuSuite.YearFrom].ShouldBeTrue();
        fields[QuSuite.SimilarTo].ShouldBeTrue();
        fields[QuSuite.MinRating].ShouldBeFalse();
        fields[QuSuite.YearTo].ShouldBeTrue();
    }

    [Theory]
    [InlineData(new[] { "Матрицу" }, new[] { "Матрица" }, true)]
    [InlineData(new[] { "Матрицу" }, new[] { "Начало" }, false)]
    [InlineData(new[] { "Toy Story", "Shrek" }, new[] { "shrek", "Toy Story" }, true)]
    [InlineData(new[] { "Alien" }, new string[0], false)]
    [InlineData(new string[0], new string[0], true)]
    public void Similar_titles_match_by_containment_ignoring_case(string[] expected, string[] actual, bool match) =>
        QuSuite.TitlesMatch(expected, actual).ShouldBe(match);

    [Fact]
    public void Options_parse_modes_overrides_and_suite_defaults()
    {
        var options = EvalOptions.Parse(["--suite", "search", "--label", "rrf-k20", "--modes", "hybrid,keyword", "--set", "Search:Hybrid:RrfK=20"]);

        options.Modes.Select(m => m.Name).ShouldBe(["hybrid", "keyword"]);
        options.Overrides["Search:Hybrid:RrfK"].ShouldBe("20");
        options.Repeat.ShouldBe(1);
        EvalOptions.Parse(["--suite", "qu", "--label", "x"]).Repeat.ShouldBe(3);
    }

    [Theory]
    [InlineData("--suite", "nope")]
    [InlineData("--suite", "search")]
    [InlineData("--suite", "search", "--label", "has space")]
    [InlineData("--suite", "search", "--label", "x", "--modes", "fuzzy")]
    [InlineData("--suite", "search", "--label", "x", "--repeat", "0")]
    [InlineData("--suite", "search", "--label", "x", "--set", "novalue")]
    public void Options_reject_bad_input(params string[] args) =>
        Should.Throw<ArgumentException>(() => EvalOptions.Parse(args));
}
