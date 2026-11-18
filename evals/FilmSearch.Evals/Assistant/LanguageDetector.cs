using System.Text.RegularExpressions;
using FilmSearch.Core.Localization;

namespace FilmSearch.Evals.Assistant;

/// <summary>
/// A crude language guess for answers (en, ru or sv): enough to see that a Russian question got a Russian answer. Bold
/// and quoted spans are removed first, because titles may stay in another language (rule 6 of the prompt). Cyrillic
/// letters mean Russian; between English and Swedish, common function words and the letters å, ä, ö decide.
/// </summary>
public static partial class LanguageDetector
{
    public const string Unknown = "unknown";

    private static readonly HashSet<string> English = new(StringComparer.Ordinal)
    {
        "the", "and", "is", "are", "to", "of", "a", "an", "you", "your", "it", "for", "with", "this", "that", "these",
        "in", "on", "here", "what", "would", "like", "can", "about", "if", "or", "be", "will", "some", "from", "at",
        "has", "have", "my", "me", "not", "but", "as", "all", "more", "one", "which", "there",
    };

    private static readonly HashSet<string> Swedish = new(StringComparer.Ordinal)
    {
        "och", "att", "det", "är", "en", "ett", "som", "för", "med", "på", "inte", "jag", "du", "har", "den", "till",
        "av", "om", "kan", "vill", "här", "eller", "några", "filmer", "filmen", "vad", "din", "dig", "mig", "också",
        "från", "så", "men", "hyra", "köpa", "varukorgen", "tips", "gillar", "handlar", "där", "när", "vi", "ni",
    };

    /// <summary><c>en</c>, <c>ru</c>, <c>sv</c> or <see cref="Unknown"/> (no words to decide by).</summary>
    public static string Detect(string text)
    {
        var plain = Marked().Replace(text, " ");
        int cyrillic = 0, latin = 0;
        foreach (var c in plain)
        {
            if (c is >= 'Ѐ' and <= 'ӿ')
            {
                cyrillic++;
            }
            else if (char.IsAsciiLetter(c) || c is 'å' or 'ä' or 'ö' or 'Å' or 'Ä' or 'Ö' or 'é')
            {
                latin++;
            }
        }

        if (cyrillic + latin == 0)
        {
            return Unknown;
        }

        if (cyrillic >= latin)
        {
            return Locales.Russian;
        }

        int english = 0, swedish = 0;
        foreach (Match word in Word().Matches(plain.ToLowerInvariant()))
        {
            if (English.Contains(word.Value))
            {
                english++;
            }

            if (Swedish.Contains(word.Value))
            {
                swedish++;
            }

            if (word.Value.AsSpan().IndexOfAny("åäö") >= 0)
            {
                swedish++;
            }
        }

        return english == 0 && swedish == 0 ? Unknown
            : swedish > english ? Locales.Swedish
            : Locales.English;
    }

    [GeneratedRegex(@"\*\*[^*\n]{1,200}?\*\*|«[^»\n]{1,200}»|“[^”\n]{1,200}”|”[^”\n]{1,200}”|""[^""\n]{1,200}""")]
    private static partial Regex Marked();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();
}
