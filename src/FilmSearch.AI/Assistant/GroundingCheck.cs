using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FilmSearch.AI.Assistant;

/// <summary>Result of <see cref="GroundingCheck"/>.</summary>
/// <param name="Titles">Movie titles found in the answer.</param>
/// <param name="Unknown">Titles that no tool returned and the user did not mention: possible hallucinations.</param>
internal sealed record GroundingResult(IReadOnlyList<string> Titles, IReadOnlyList<string> Unknown);

/// <summary>
/// Grounding post-check (spec 06 §5): are the movies named in the answer ones that tools returned? It is a metric and a
/// log line, not a filter: the answer is already streamed, and the cards come from tool results anyway.
/// <para>
/// Heuristic: the prompt asks for <c>**bold titles**</c>, so bold spans and quoted spans («…», “…”, ”…”) are the
/// candidates; labels (<c>**Total:**</c>), prices and long phrases are skipped. A candidate is grounded when, after
/// normalization (case, diacritics, punctuation, a trailing year), it appears as whole words in the reference texts:
/// tool results of this turn and earlier ones, the system prompt with the rolling summary, and the user's messages (the
/// user may name a movie that is not in the catalog, and the answer may repeat it). Known gaps: titles written without
/// markup are missed; a short bold word that occurs in an overview counts as grounded.
/// </para>
/// <para>
/// Tuned on the I14 evaluation: a bold span that starts with a lower-case letter is emphasis («детская», «взрослая»), not
/// a title, and is skipped, like a runtime (**148 minutes**); a Cyrillic candidate is also grounded when every word of
/// it shares a prefix with a word of one reference text (all letters but the last two of the shorter word, at least
/// three), so an inflected title («Матрицей», «Ла-Ла Ленде» for «Матрица», «Ла-Ла Ленд») counts as grounded. Original
/// titles are in the tool results (<c>originalTitle</c>), so «Иван Васильевич меняет профессию» for the English title
/// is grounded too. Gaps: a title that starts with a lower-case letter (mother!) is missed; a transliterated name
/// («Фрэнсис Форд Коппола» for "Francis Ford Coppola") counts as unknown.
/// </para>
/// </summary>
internal static partial class GroundingCheck
{
    private const int MaxCandidateLength = 120;
    private const int MaxCandidateWords = 12;

    public static GroundingResult Check(string answer, IEnumerable<string> references)
    {
        var normalizedReferences = references.Select(Normalize).ToList();
        var haystack = " " + string.Join(" | ", normalizedReferences) + " ";
        List<string[]>? referenceWords = null;
        var titles = new List<string>();
        var unknown = new List<string>();

        foreach (var candidate in Candidates(answer))
        {
            if (titles.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            titles.Add(candidate);
            var normalized = Normalize(candidate);
            if (normalized.Length > 0
                && !haystack.Contains($" {normalized} ", StringComparison.Ordinal)
                && !(normalized.Any(IsCyrillic) && MatchesInflected(normalized, referenceWords ??= [.. normalizedReferences.Select(r => r.Split(' '))])))
            {
                unknown.Add(candidate);
            }
        }

        return new GroundingResult(titles, unknown);
    }

    internal static IEnumerable<string> Candidates(string answer)
    {
        foreach (Match match in Marked().Matches(answer))
        {
            var text = (match.Groups["bold"].Success ? match.Groups["bold"]
                : match.Groups["guillemets"].Success ? match.Groups["guillemets"]
                : match.Groups["curly"].Success ? match.Groups["curly"]
                : match.Groups["swedish"]).Value.Trim();

            // A label such as **Total:** or **Why it fits:**, or emphasis such as **детская** (titles start with a capital).
            if (text.EndsWith(':') || (text.Length > 0 && char.IsLower(text[0])))
            {
                continue;
            }

            text = TrailingYear().Replace(text, "").Trim().TrimEnd('.', ',', ';', '!', '—', '–', '-').Trim();
            if (text.Length < 2
                || text.Length > MaxCandidateLength
                || !text.Any(char.IsLetter)
                || text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxCandidateWords
                || Price().IsMatch(text)
                || Runtime().IsMatch(text))
            {
                continue;
            }

            yield return text;
        }
    }

    /// <summary>Every word of the candidate has a word with the same stem in one reference text (Russian inflection).</summary>
    private static bool MatchesInflected(string normalized, List<string[]> references)
    {
        var words = normalized.Split(' ');
        return references.Any(reference => words.All(w => reference.Any(r => SameStem(w, r))));
    }

    /// <summary>Same word up to the ending: a common prefix of all but the last two letters of the shorter word, at least three.</summary>
    internal static bool SameStem(string a, string b)
    {
        if (a == b)
        {
            return true;
        }

        var common = 0;
        while (common < a.Length && common < b.Length && a[common] == b[common])
        {
            common++;
        }

        return common >= Math.Max(3, Math.Min(a.Length, b.Length) - 2);
    }

    private static bool IsCyrillic(char c) => c is >= '\u0400' and <= '\u04FF';

    /// <summary>Lower case, no diacritics (ё → е, é → e), letters and digits only, single spaces.</summary>
    internal static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var space = true;
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                space = false;
            }
            else if (!space)
            {
                builder.Append(' ');
                space = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    [GeneratedRegex(@"\*\*(?<bold>[^*\n]{1,200}?)\*\*|«(?<guillemets>[^»\n]{1,200})»|“(?<curly>[^”\n]{1,200})”|”(?<swedish>[^”\n]{1,200})”")]
    private static partial Regex Marked();

    [GeneratedRegex(@"\s*\(\s*\d{4}\s*\)\s*$")]
    private static partial Regex TrailingYear();

    [GeneratedRegex(@"\d+[.,]\d{2}")]
    private static partial Regex Price();

    [GeneratedRegex(@"^\d+\s*(min\w*|minut\w*|минут\w*|мин\.?|h|hours?|ч\.?|час\w*|tim\w*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Runtime();
}
