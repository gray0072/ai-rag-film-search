using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FilmSearch.AI.Telemetry;

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
/// markup are missed; a short bold word that occurs in an overview counts as grounded; an inflected title (Russian
/// «Ла-Ла Ленде» for «Ла-Ла Ленд») counts as unknown. I14 measures it on the evaluation set.
/// </para>
/// </summary>
internal static partial class GroundingCheck
{
    private const int MaxCandidateLength = 120;
    private const int MaxCandidateWords = 12;

    public static GroundingResult Check(string answer, IEnumerable<string> references)
    {
        var haystack = " " + string.Join(" | ", references.Select(Normalize)) + " ";
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
            if (normalized.Length > 0 && !haystack.Contains($" {normalized} ", StringComparison.Ordinal))
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

            // A label such as **Total:** or **Why it fits:**.
            if (text.EndsWith(':'))
            {
                continue;
            }

            text = TrailingYear().Replace(text, "").Trim().TrimEnd('.', ',', ';', '!', '—', '–', '-').Trim();
            if (text.Length < 2
                || text.Length > MaxCandidateLength
                || !text.Any(char.IsLetter)
                || text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxCandidateWords
                || Price().IsMatch(text))
            {
                continue;
            }

            yield return text;
        }
    }

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
}

/// <summary>Metrics of the assistant (meter <c>FilmSearch</c>, spec 10 §3).</summary>
public sealed class AssistantMetrics
{
    private readonly Counter<long> _titles;

    public AssistantMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(AiMetrics.MeterName);
        _titles = meter.CreateCounter<long>(
            "assistant.grounding.titles",
            unit: "{title}",
            description: "Movie titles named in assistant answers; grounded=false when no tool returned the movie (possible hallucination).");
    }

    internal void RecordGrounding(GroundingResult result, string promptId)
    {
        var prompt = new KeyValuePair<string, object?>("prompt", promptId);
        var grounded = result.Titles.Count - result.Unknown.Count;
        if (grounded > 0)
        {
            _titles.Add(grounded, prompt, new("grounded", true));
        }

        if (result.Unknown.Count > 0)
        {
            _titles.Add(result.Unknown.Count, prompt, new("grounded", false));
        }
    }
}
