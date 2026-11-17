using System.Text.RegularExpressions;

namespace FilmSearch.Infrastructure.Search.Keyword;

/// <summary>Turns user text into input for <c>websearch_to_tsquery</c>.</summary>
internal static partial class KeywordQuery
{
    /// <summary>Folds <c>ё</c> to <c>е</c> like the indexed documents, and trims.</summary>
    public static string Normalize(string text) =>
        text.Replace('ё', 'е').Replace('Ё', 'Е').Trim();

    /// <summary>
    /// The query relaxed to match any of its words: <c>nolan dream</c> → <c>nolan or dream</c>
    /// (<c>websearch_to_tsquery</c> syntax). Quotes and <c>-</c> are dropped: only letters and digits are kept.
    /// Returns null when there are fewer than two words, so relaxing would change nothing.
    /// </summary>
    public static string? AnyWord(string text)
    {
        var words = Word().Matches(text)
            .Select(m => m.Value)
            .Where(w => !w.Equals("or", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return words.Count < 2 ? null : string.Join(" or ", words);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
}
