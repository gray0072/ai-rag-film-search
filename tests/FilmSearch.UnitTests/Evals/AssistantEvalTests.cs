using FilmSearch.Evals;
using FilmSearch.Evals.Assistant;
using FilmSearch.Evals.Datasets;
using FilmSearch.Evals.Reports;

namespace FilmSearch.UnitTests.Evals;

/// <summary>The assistant evaluation (I14): datasets, deterministic checks, language guess, judge output and summary math.</summary>
public sealed class AssistantEvalTests
{
    private static readonly string Root = RepositoryPaths.FindRoot();
    private static readonly SeedCatalog Seed = DatasetLoader.LoadSeed(RepositoryPaths.SeedFile(Root));

    private static readonly SeedCatalog TinySeed = new(
        new Dictionary<int, IReadOnlySet<string>> { [1] = new HashSet<string> { "horror" }, [2] = new HashSet<string> { "drama" } },
        new HashSet<string> { "horror", "drama" });

    [Theory]
    [InlineData(DatasetLoader.AssistantScenariosFileName, DatasetLoader.MinScenarioCases)]
    [InlineData(DatasetLoader.AssistantAdversarialFileName, DatasetLoader.MinAdversarialCases)]
    public void Assistant_datasets_are_valid(string fileName, int minCases)
    {
        var dataset = DatasetLoader.Load<AssistantCase>(Path.Combine(RepositoryPaths.Datasets(Root), fileName));

        DatasetLoader.ValidateAssistant(dataset.Cases, Seed, minCases).ShouldBeEmpty();
        dataset.Cases.Count.ShouldBeGreaterThanOrEqualTo(minCases);
    }

    [Fact]
    public void Scenarios_include_real_conversations_and_adversarial_cases_cover_the_spec_risks()
    {
        var scenarios = DatasetLoader.Load<AssistantCase>(Path.Combine(RepositoryPaths.Datasets(Root), DatasetLoader.AssistantScenariosFileName));
        var adversarial = DatasetLoader.Load<AssistantCase>(Path.Combine(RepositoryPaths.Datasets(Root), DatasetLoader.AssistantAdversarialFileName));

        scenarios.Cases.Count(c => c.Tags.Contains(AssistantTags.Real)).ShouldBeGreaterThanOrEqualTo(3);
        var tags = adversarial.Cases.SelectMany(c => c.Tags).ToHashSet();
        new[] { AssistantTags.Injection, AssistantTags.OffTopic, AssistantTags.Price, AssistantTags.Payment, AssistantTags.LongInput }
            .Except(tags).ShouldBeEmpty();
    }

    [Fact]
    public void Validation_reports_unknown_tools_movies_genres_and_bad_patterns()
    {
        AssistantCase[] cases =
        [
            new("a", "de", [
                new AssistantTurn("", new TurnExpectation
                {
                    ToolsCalled = ["search_movies", "buy_now"],
                    ToolsNotCalled = ["search_movies"],
                    MoviesAnyOf = [99],
                    CardsExcludeGenres = ["musical"],
                    Cart = new CartExpectation(Offer: "lease"),
                    AnswerMatches = ["(unclosed"],
                    Language = "de",
                }),
            ], ["unknown-tag"], Setup: new AssistantSetup([new SetupCartLine(98, "gift")])),
            new("a", "en", [], []),
        ];

        var errors = DatasetLoader.ValidateAssistant(cases, TinySeed, minCases: 3);

        errors.ShouldContain("a: duplicate id.");
        errors.ShouldContain("a: locale 'de' is not one of en, ru, sv.");
        errors.ShouldContain("a: unknown tag 'unknown-tag'.");
        errors.ShouldContain("a: turns are empty.");
        errors.ShouldContain("a: setup cart movie 98 is not in the seed.");
        errors.ShouldContain("a: setup cart offer 'gift' is not buy or rent.");
        errors.ShouldContain(e => e.Contains("turn 1: user text must be", StringComparison.Ordinal));
        errors.ShouldContain("a: turn 1: unknown tool 'buy_now'.");
        errors.ShouldContain("a: turn 1: a tool is both required and forbidden.");
        errors.ShouldContain("a: turn 1: movie 99 is not in the seed.");
        errors.ShouldContain("a: turn 1: genre 'musical' is not a genre slug of the seed.");
        errors.ShouldContain("a: turn 1: cart offer 'lease' is not buy or rent.");
        errors.ShouldContain("a: turn 1: language 'de' is not one of en, ru, sv or any.");
        errors.ShouldContain(e => e.StartsWith("a: turn 1: pattern '(unclosed' is not a valid regular expression", StringComparison.Ordinal));
        errors.ShouldContain("Only 2 cases; at least 3 are needed (spec 09 §2).");
    }

    [Theory]
    [InlineData("Here are a few space movies you might like: **Interstellar** (2014) is a great pick.", "en")]
    [InlineData("Вот несколько фильмов про космос: **Interstellar** (2014) — путешествие сквозь червоточину.", "ru")]
    [InlineData("Här är några rymdfilmer som du kanske gillar: **Interstellar** (2014) och **The Martian** (2015).", "sv")]
    [InlineData("Jag har lagt **The Matrix** i varukorgen, tryck på knappen för att betala.", "sv")]
    [InlineData("**Интерстеллар** — great choice, it is about a wormhole and the future of humanity.", "en")]
    [InlineData("**Interstellar** (2014)", "unknown")]
    public void Language_is_guessed_without_the_titles(string answer, string expected) =>
        LanguageDetector.Detect(answer).ShouldBe(expected);

    [Fact]
    public void Global_checks_apply_to_every_answer()
    {
        var record = Turn("Here are some movies you might like.");

        TurnChecks.Evaluate("en", new AssistantTurn("hi"), record);

        record.Checks.Select(c => c.Name).ShouldBe([TurnChecks.Answered, TurnChecks.Language, TurnChecks.Grounding, TurnChecks.NoOrder, TurnChecks.Iterations]);
        record.Passed.ShouldBeTrue();
    }

    [Fact]
    public void Failed_global_checks_name_the_problem()
    {
        var record = Turn("Вот фильмы.");
        record.UnknownTitles = ["Galactic Llama Wars"];
        record.OrdersAfter = 1;
        record.IterationLimitHit = true;

        TurnChecks.Evaluate("en", new AssistantTurn("hi"), record);

        Failed(record).ShouldBe(new Dictionary<string, string?>
        {
            [TurnChecks.Language] = "answered in ru, expected en",
            [TurnChecks.Grounding] = "not from tools: Galactic Llama Wars",
            [TurnChecks.NoOrder] = "1 order(s) created without the user's click",
            [TurnChecks.Iterations] = "tool iteration limit hit",
        });
    }

    [Fact]
    public void An_error_event_fails_the_turn_and_skips_the_language_check()
    {
        var record = Turn("");
        record.Error = "providerUnavailable: down";

        TurnChecks.Evaluate("en", new AssistantTurn("hi"), record);

        Failed(record)[TurnChecks.Answered].ShouldBe("error event: providerUnavailable: down");
        record.Checks.ShouldNotContain(c => c.Name == TurnChecks.Language);
    }

    [Fact]
    public void Tool_expectations_check_required_and_forbidden_tools()
    {
        var record = Turn("I added it to your cart.");
        record.Tools.Add(new ToolCallRecord("add_to_cart", "{}", true));

        TurnChecks.Evaluate("en", new AssistantTurn("buy it", new TurnExpectation
        {
            ToolsCalled = ["add_to_cart", "request_checkout"],
            ToolsNotCalled = ["remove_from_cart", "add_to_cart"],
        }), record);

        Failed(record)[TurnChecks.Tools].ShouldBe("not called: request_checkout; called: add_to_cart");
    }

    [Fact]
    public void No_tool_at_all_is_written_as_a_star()
    {
        var record = Turn("Sorry, I can only help with movies.");
        record.Tools.Add(new ToolCallRecord("search_movies", "{}", true));

        TurnChecks.Evaluate("en", new AssistantTurn("weather?", new TurnExpectation { ToolsNotCalled = ["*"] }), record);

        Failed(record)[TurnChecks.Tools].ShouldBe("called: search_movies");
    }

    [Fact]
    public void Card_expectations_check_genres_years_and_counts()
    {
        var record = Turn("Here are some thrillers you might like.");
        record.Cards.Add(new CardRecord(1, "Scary", 1995, ["horror", "thriller"]));
        record.Cards.Add(new CardRecord(2, "Old", 1985, ["thriller"]));

        TurnChecks.Evaluate("en", new AssistantTurn("90s thriller, no horror", new TurnExpectation
        {
            CardsExcludeGenres = ["horror"],
            CardsYearFrom = 1990,
            CardsYearTo = 1999,
            MinCards = 3,
        }), record);

        Failed(record)[TurnChecks.Cards].ShouldBe("Scary: horror; Old (1985); 2 cards, at least 3");
    }

    [Fact]
    public void Movie_expectation_accepts_a_card_or_a_cart_line()
    {
        var record = Turn("It is in your cart.");
        record.CartAfter.Add(new CartLineRecord(105, "Back to the Future", "buy"));

        TurnChecks.Evaluate("en", new AssistantTurn("buy it", new TurnExpectation { MoviesAnyOf = [105, 862] }), record);

        record.Passed.ShouldBeTrue();
    }

    [Fact]
    public void Cart_expectations_check_lines_offers_count_and_changes()
    {
        var record = Turn("I added it to your cart.");
        record.CartAfter.Add(new CartLineRecord(105, "Back to the Future", "rent"));
        record.CartAfter.Add(new CartLineRecord(862, "Toy Story", "buy"));
        record.CartUpdated = true;

        TurnChecks.Evaluate("en", new AssistantTurn("buy it", new TurnExpectation
        {
            Cart = new CartExpectation(Contains: [105, 27205], NotContains: [862], Offer: "buy", Count: 1, Unchanged: true),
        }), record);

        Failed(record)[TurnChecks.Cart].ShouldBe("Back to the Future: rent, expected buy; 27205 not in the cart; Toy Story is in the cart; 2 lines, expected 1; the cart was changed");
    }

    [Fact]
    public void Confirmation_and_text_expectations()
    {
        var record = Turn("Your order is paid. Enjoy the movie!");

        TurnChecks.Evaluate("en", new AssistantTurn("pay", new TurnExpectation
        {
            Confirmation = true,
            AnswerMatches = ["go to payment"],
            AnswerNotMatches = ["(is|has been) paid"],
        }), record);

        Failed(record)[TurnChecks.Confirmation].ShouldBe("no checkout confirmation");
        Failed(record)[TurnChecks.Text].ShouldBe("no match for /go to payment/; matches /(is|has been) paid/");
    }

    [Fact]
    public void Language_any_and_another_language_per_turn()
    {
        var russian = Turn("Вот фильмы для детей.");
        TurnChecks.Evaluate("en", new AssistantTurn("А для детей?", new TurnExpectation { Language = "ru" }), russian);
        russian.Passed.ShouldBeTrue();

        var any = Turn("Вот фильмы для детей.");
        TurnChecks.Evaluate("en", new AssistantTurn("?", new TurnExpectation { Language = "any" }), any);
        any.Checks.ShouldNotContain(c => c.Name == TurnChecks.Language);
    }

    [Fact]
    public void Judge_output_is_parsed_and_validated()
    {
        AssistantJudge.TryParse("""{"comment":"Good picks.","relevance":5,"explanations":4,"tone":5,"constraints":3}""", out var scores, out _).ShouldBeTrue();
        scores.ShouldBe(new JudgeScores(5, 4, 5, 3, "Good picks."));
        scores!.Mean.ShouldBe(4.25);

        AssistantJudge.TryParse("```json\n{\"comment\":\"\",\"relevance\":1,\"explanations\":1,\"tone\":1,\"constraints\":1}\n```", out _, out _).ShouldBeTrue();

        AssistantJudge.TryParse("""{"comment":"x","relevance":6,"explanations":4,"tone":5,"constraints":3}""", out _, out var range).ShouldBeFalse();
        range.ShouldBe("every score must be an integer from 1 to 5.");

        AssistantJudge.TryParse("Five out of five!", out _, out var json).ShouldBeFalse();
        json.ShouldStartWith("not valid JSON");
    }

    [Fact]
    public void Judge_transcript_shows_tools_cards_cart_and_the_checkout_button_as_data()
    {
        var assistantCase = new AssistantCase("c", "en", [new AssistantTurn("Take the second one, buy it.")], ["checkout"], Judge: "Adds the second movie.");
        var record = new TurnRecord { Number = 1, User = "Take the second one, buy it.", Answer = "Press **Go to payment**." };
        record.Tools.Add(new ToolCallRecord("add_to_cart", """{"offerType":"buy"}""", true));
        record.Tools.Add(new ToolCallRecord("request_checkout", "", false));
        record.Cards.Add(new CardRecord(597, "Titanic", 1997, ["drama", "romance"]));
        record.CartAfter.Add(new CartLineRecord(597, "Titanic", "buy"));
        record.CartUpdated = true;
        record.ConfirmationRequired = true;
        record.ConfirmationTotal = "9.99 USD";

        var transcript = AssistantJudge.Transcript(assistantCase, [record]);

        transcript.ShouldContain("What a good conversation does here: Adds the second movie.");
        transcript.ShouldContain("<transcript>");
        transcript.ShouldContain("Customer: Take the second one, buy it.");
        transcript.ShouldContain("""Tool call: add_to_cart {"offerType":"buy"}""");
        transcript.ShouldContain("Tool call: request_checkout  (refused or failed)");
        transcript.ShouldContain("Movies returned by tools (cards): Titanic (1997) [drama, romance]");
        transcript.ShouldContain("Cart after the turn: Titanic (buy)");
        transcript.ShouldContain("Checkout button shown to the customer, total 9.99 USD");
        transcript.ShouldContain("Assistant: Press **Go to payment**.");
        transcript.TrimEnd().ShouldEndWith("</transcript>");
    }

    [Fact]
    public void Summary_gives_pass_rate_per_repeat_with_its_spread_and_flaky_cases()
    {
        var stable = Result("stable", Run(1, passed: true, judge: 5), Run(2, passed: true, judge: 4));
        var flaky = Result("flaky", Run(1, passed: true, judge: 3), Run(2, passed: false, judge: 2, unknownTitle: "Fake"));

        var summary = AssistantSummary.Of([stable, flaky]);

        // Repeat 1: 2/2 cases pass; repeat 2: 1/2.
        summary.PassRate.Mean.ShouldBe(0.75);
        summary.PassRate.StdDev.ShouldBe(Math.Sqrt(0.125), 1e-9);
        (summary.StableCases, summary.FlakyCases, summary.FailingCases).ShouldBe((1, 1, 0));
        summary.GroundingViolationRate.ShouldBe(0.25);
        (summary.Titles, summary.UnknownTitles).ShouldBe((8, 1));
        summary.JudgeMean.Mean.ShouldBe(3.5);
        summary.Checks[TurnChecks.Grounding].ShouldBe((4, 0.75));
    }

    [Fact]
    public void Options_parse_the_assistant_suite()
    {
        var options = EvalOptions.Parse([
            "--suite", "adversarial", "--label", "x", "--judge-profile", "openrouter", "--judge-model", "openai/gpt-6-luna",
            "--cases", "adv-en-*, adv-ru-essay", "--parallel", "4",
        ]);

        options.IsAssistantSuite.ShouldBeTrue();
        options.Repeat.ShouldBe(3);
        (options.JudgeProfile, options.JudgeModel, options.NoJudge, options.Parallel).ShouldBe(("openrouter", "openai/gpt-6-luna", false, 4));
        options.IncludesCase("adv-en-code").ShouldBeTrue();
        options.IncludesCase("adv-ru-essay").ShouldBeTrue();
        options.IncludesCase("adv-ru-stocks").ShouldBeFalse();
        EvalOptions.Parse(["--suite", "assistant", "--label", "x", "--no-judge"]).NoJudge.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => EvalOptions.Parse(["--suite", "assistant", "--label", "x", "--parallel", "0"]));
    }

    private static TurnRecord Turn(string answer) => new() { Number = 1, User = "user", Answer = answer };

    private static Dictionary<string, string?> Failed(TurnRecord record) =>
        record.Checks.Where(c => !c.Passed).ToDictionary(c => c.Name, c => c.Detail);

    private static AssistantCaseResult Result(string id, params AssistantRun[] runs) =>
        new(new AssistantCase(id, "en", [new AssistantTurn("hi")], ["recommend"]), runs);

    private static AssistantRun Run(int repeat, bool passed, int judge, string? unknownTitle = null)
    {
        var turn = Turn("answer");
        turn.Titles = ["A", "B"];
        turn.UnknownTitles = unknownTitle is null ? [] : [unknownTitle];
        turn.Checks.Add(new CheckResult(TurnChecks.Grounding, unknownTitle is null));
        turn.Checks.Add(new CheckResult(TurnChecks.Answered, passed));
        return new AssistantRun(repeat, [turn], new JudgeScores(judge, judge, judge, judge, ""), null, 1000);
    }
}
