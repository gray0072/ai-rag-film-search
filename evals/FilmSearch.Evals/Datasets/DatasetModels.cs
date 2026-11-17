using System.Globalization;

namespace FilmSearch.Evals.Datasets;

/// <summary>
/// One line of <c>evals/datasets/search.golden.jsonl</c> (spec 09 §2). Movies are TMDB ids: they are stable across
/// databases, while catalog ids (GUIDs) are created when the seed is loaded.
/// </summary>
/// <param name="Id">Unique case id, e.g. <c>ru-plot-03</c>.</param>
/// <param name="Query">The user's query as typed.</param>
/// <param name="Locale">Language of the query: <c>en</c>, <c>ru</c> or <c>sv</c>. Also the locale of the search request.</param>
/// <param name="Relevant">TMDB ids of the relevant movies.</param>
/// <param name="Tags">Query types (<see cref="SearchTags"/>), for the breakdown in the report.</param>
/// <param name="Graded">Optional grades 1–3 of relevant movies; a relevant movie without a grade has grade 1.</param>
/// <param name="ExcludedGenres">Genre slugs the query rules out ("no horror"); results with them count as violations.</param>
public sealed record SearchCase(
    string Id,
    string Query,
    string Locale,
    IReadOnlyList<int> Relevant,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, int>? Graded = null,
    IReadOnlyList<string>? ExcludedGenres = null)
{
    /// <summary>Grade of every relevant movie by TMDB id.</summary>
    public IReadOnlyDictionary<int, int> Grades()
    {
        var grades = new Dictionary<int, int>();
        foreach (var id in Relevant)
        {
            grades[id] = Graded is not null && Graded.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var grade) ? grade : 1;
        }

        return grades;
    }
}

/// <summary>Allowed tags of search cases (spec 09 §2).</summary>
public static class SearchTags
{
    public const string Plot = "plot";
    public const string Mood = "mood";
    public const string Person = "person";
    public const string TitleTypo = "title-typo";
    public const string Constraint = "constraint";
    public const string Negative = "negative";

    /// <summary>The query is in ru or sv and the words to match exist only in other languages (no overview in the query's language, a transliterated name).</summary>
    public const string CrossLanguage = "cross-language";

    public static IReadOnlyList<string> All { get; } = [Plot, Mood, Person, TitleTypo, Constraint, Negative, CrossLanguage];
}

/// <summary>One line of <c>evals/datasets/qu.golden.jsonl</c>: a query and the output query understanding should give.</summary>
public sealed record QuCase(string Id, string Query, string Locale, QuExpected Expected, IReadOnlyList<string> Tags);

/// <summary>Expected filters; null and empty mean "not in the query", and the model must not invent them.</summary>
public sealed record QuExpected
{
    public IReadOnlyList<string> GenresInclude { get; init; } = [];

    public IReadOnlyList<string> GenresExclude { get; init; } = [];

    public int? YearFrom { get; init; }

    public int? YearTo { get; init; }

    public decimal? MinRating { get; init; }

    public long? MaxPriceMinor { get; init; }

    public string? Language { get; init; }

    /// <summary>Titles of "movies like X"; a predicted title matches when one contains the other, ignoring case.</summary>
    public IReadOnlyList<string> SimilarToTitles { get; init; } = [];
}
