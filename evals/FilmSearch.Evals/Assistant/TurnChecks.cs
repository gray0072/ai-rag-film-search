using System.Text.RegularExpressions;
using FilmSearch.Evals.Datasets;

namespace FilmSearch.Evals.Assistant;

/// <summary>
/// Deterministic checks of one answer (spec 09 §3). A check is recorded only when it applies to the turn, so the pass
/// rate of a check is over the turns that test it. Every answer gets <see cref="Answered"/>, <see cref="Grounding"/>,
/// <see cref="NoOrder"/>, <see cref="Iterations"/> and (unless the case says <c>any</c>) <see cref="Language"/>.
/// </summary>
public static class TurnChecks
{
    public const string Answered = "answered";
    public const string Tools = "tools";
    public const string Movies = "movies";
    public const string Cards = "cards";
    public const string Cart = "cart";
    public const string Confirmation = "confirmation";
    public const string Language = "language";
    public const string Text = "text";
    public const string Grounding = "grounding";
    public const string NoOrder = "no-order";
    public const string Iterations = "iterations";

    public static IReadOnlyList<string> All { get; } = [Answered, Tools, Movies, Cards, Cart, Confirmation, Language, Text, Grounding, NoOrder, Iterations];

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Runs the checks of <paramref name="turn"/> on <paramref name="record"/> and adds the results to it.</summary>
    public static void Evaluate(string caseLocale, AssistantTurn turn, TurnRecord record)
    {
        var e = turn.Expect ?? new TurnExpectation();
        var checks = record.Checks;

        checks.Add(record.Error is not null
            ? new CheckResult(Answered, false, $"error event: {record.Error}")
            : string.IsNullOrWhiteSpace(record.Answer)
                ? new CheckResult(Answered, false, "empty answer")
                : new CheckResult(Answered, true));

        var called = record.Tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        if (e.ToolsCalled.Count > 0 || e.ToolsNotCalled.Count > 0)
        {
            var problems = new List<string>();
            var missing = e.ToolsCalled.Where(t => !called.Contains(t)).ToList();
            if (missing.Count > 0)
            {
                problems.Add($"not called: {string.Join(", ", missing)}");
            }

            var forbidden = e.ToolsNotCalled.Contains(AssistantToolNames.Any)
                ? [.. called]
                : e.ToolsNotCalled.Where(called.Contains).ToList();
            if (forbidden.Count > 0)
            {
                problems.Add($"called: {string.Join(", ", forbidden)}");
            }

            checks.Add(Result(Tools, problems));
        }

        if (e.MoviesAnyOf.Count > 0)
        {
            var shown = record.Cards.Select(c => c.TmdbId).Concat(record.CartAfter.Select(l => l.TmdbId)).ToHashSet();
            checks.Add(e.MoviesAnyOf.Any(shown.Contains)
                ? new CheckResult(Movies, true)
                : new CheckResult(Movies, false, $"none of {string.Join(", ", e.MoviesAnyOf)} shown"));
        }

        if (e.CardsExcludeGenres.Count > 0 || e.CardsYearFrom is not null || e.CardsYearTo is not null || e.MinCards is not null || e.MaxCards is not null)
        {
            var problems = new List<string>();
            foreach (var card in record.Cards)
            {
                var genres = card.GenreSlugs.Intersect(e.CardsExcludeGenres).ToList();
                if (genres.Count > 0)
                {
                    problems.Add($"{card.Title}: {string.Join(", ", genres)}");
                }

                if ((e.CardsYearFrom is { } from && card.Year < from) || (e.CardsYearTo is { } to && card.Year > to))
                {
                    problems.Add($"{card.Title} ({card.Year})");
                }
            }

            if (e.MinCards is { } min && record.Cards.Count < min)
            {
                problems.Add($"{record.Cards.Count} cards, at least {min}");
            }

            if (e.MaxCards is { } max && record.Cards.Count > max)
            {
                problems.Add($"{record.Cards.Count} cards, at most {max}");
            }

            checks.Add(Result(Cards, problems));
        }

        if (e.Cart is { } cart)
        {
            var problems = new List<string>();
            var lines = record.CartAfter.ToDictionary(l => l.TmdbId);
            foreach (var id in cart.Contains ?? [])
            {
                if (!lines.TryGetValue(id, out var line))
                {
                    problems.Add($"{id} not in the cart");
                }
                else if (cart.Offer is { } offer && line.Offer != offer)
                {
                    problems.Add($"{line.Title}: {line.Offer}, expected {offer}");
                }
            }

            foreach (var id in (cart.NotContains ?? []).Where(lines.ContainsKey))
            {
                problems.Add($"{lines[id].Title} is in the cart");
            }

            if (cart.Count is { } count && record.CartAfter.Count != count)
            {
                problems.Add($"{record.CartAfter.Count} lines, expected {count}");
            }

            if (cart.Unchanged == true && record.CartUpdated)
            {
                problems.Add("the cart was changed");
            }

            checks.Add(Result(Cart, problems));
        }

        if (e.Confirmation is { } confirmation)
        {
            checks.Add(record.ConfirmationRequired == confirmation
                ? new CheckResult(Confirmation, true)
                : new CheckResult(Confirmation, false, confirmation ? "no checkout confirmation" : "asked for checkout confirmation"));
        }

        var language = e.Language ?? caseLocale;
        if (language != "any" && record.Error is null && !string.IsNullOrWhiteSpace(record.Answer))
        {
            record.DetectedLanguage = LanguageDetector.Detect(record.Answer);
            checks.Add(record.DetectedLanguage == language
                ? new CheckResult(Language, true)
                : new CheckResult(Language, false, $"answered in {record.DetectedLanguage}, expected {language}"));
        }

        if (e.AnswerMatches.Count > 0 || e.AnswerNotMatches.Count > 0)
        {
            var problems = e.AnswerMatches.Where(p => !Matches(record.Answer, p)).Select(p => $"no match for /{p}/")
                .Concat(e.AnswerNotMatches.Where(p => Matches(record.Answer, p)).Select(p => $"matches /{p}/"))
                .ToList();
            checks.Add(Result(Text, problems));
        }

        checks.Add(record.UnknownTitles.Count == 0
            ? new CheckResult(Grounding, true)
            : new CheckResult(Grounding, false, $"not from tools: {string.Join(" | ", record.UnknownTitles)}"));

        checks.Add(record.OrdersAfter == 0
            ? new CheckResult(NoOrder, true)
            : new CheckResult(NoOrder, false, $"{record.OrdersAfter} order(s) created without the user's click"));

        var iterationProblems = new List<string>();
        if (record.IterationLimitHit)
        {
            iterationProblems.Add("tool iteration limit hit");
        }

        if (e.MaxModelCalls is { } maxCalls && record.ModelCalls > maxCalls)
        {
            iterationProblems.Add($"{record.ModelCalls} model calls, at most {maxCalls}");
        }

        checks.Add(Result(Iterations, iterationProblems));
    }

    private static bool Matches(string text, string pattern) =>
        Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static CheckResult Result(string name, List<string> problems) =>
        problems.Count == 0 ? new CheckResult(name, true) : new CheckResult(name, false, string.Join("; ", problems));
}
