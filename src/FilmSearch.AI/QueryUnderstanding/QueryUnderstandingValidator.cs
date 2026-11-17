using System.Globalization;
using System.Text.Json;
using FilmSearch.Core.Localization;
using FilmSearch.Core.Search;

namespace FilmSearch.AI.QueryUnderstanding;

/// <summary>
/// Parses and validates the model's answer. Structured output makes the shape likely but not certain (not every provider
/// enforces the schema, and the schema cannot say "yearFrom ≤ yearTo"), so every answer is checked here. The error text
/// goes back to the model on the retry, so it says exactly what to fix.
/// </summary>
internal static class QueryUnderstandingValidator
{
    public const int MaxSemanticQueryLength = 500;
    public const int MaxTitles = 5;
    public const int MaxTitleLength = 200;
    public const int MinYear = 1870;
    public const int MaxYear = 2100;
    public const long MaxPriceMinor = 1_000_000;

    public static bool TryParse(string? text, IReadOnlySet<string> genreSlugs, out UnderstoodQuery? result, out string error)
    {
        result = null;
        QueryUnderstandingOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<QueryUnderstandingOutput>(StripCodeFence(text), QueryUnderstandingSchema.JsonOptions);
        }
        catch (JsonException ex)
        {
            error = $"The answer is not valid JSON for the schema: {ex.Message}";
            return false;
        }

        if (output is null)
        {
            error = "The answer is empty or null; return a JSON object.";
            return false;
        }

        var errors = new List<string>();
        var semanticQuery = (output.SemanticQuery ?? "").Trim();
        if (semanticQuery.Length > MaxSemanticQueryLength)
        {
            errors.Add($"semanticQuery is longer than {MaxSemanticQueryLength} characters.");
        }

        var filters = output.Filters;
        if (filters is null)
        {
            errors.Add("filters is missing.");
        }

        var include = Genres(filters?.GenresInclude, "genresInclude", genreSlugs, errors);
        var exclude = Genres(filters?.GenresExclude, "genresExclude", genreSlugs, errors);
        var both = include.Intersect(exclude, StringComparer.Ordinal).ToList();
        if (both.Count > 0)
        {
            errors.Add($"Genres {string.Join(", ", both)} are both in genresInclude and genresExclude.");
        }

        Year(filters?.YearFrom, "yearFrom", errors);
        Year(filters?.YearTo, "yearTo", errors);
        if (filters?.YearFrom > filters?.YearTo)
        {
            errors.Add("yearFrom is greater than yearTo.");
        }

        if (filters?.MinRating is < 0 or > 10)
        {
            errors.Add("minRating must be between 0 and 10.");
        }

        if (filters?.MaxPriceMinor is < 0 or > MaxPriceMinor)
        {
            errors.Add($"maxPriceMinor must be between 0 and {MaxPriceMinor} (cents).");
        }

        var language = string.IsNullOrWhiteSpace(filters?.Language) ? null : filters.Language.Trim().ToLowerInvariant();
        if (language is not null && (language.Length != 2 || !language.All(char.IsAsciiLetterLower)))
        {
            errors.Add($"language '{filters!.Language}' is not a two-letter ISO 639-1 code.");
        }

        var titles = (output.SimilarToTitles ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (titles.Count > MaxTitles || titles.Any(t => t.Length > MaxTitleLength))
        {
            errors.Add($"similarToTitles must have at most {MaxTitles} titles of at most {MaxTitleLength} characters.");
        }

        if (errors.Count > 0)
        {
            error = string.Join(" ", errors);
            return false;
        }

        var locale = output.DetectedLocale?.Trim().ToLowerInvariant();
        result = new UnderstoodQuery
        {
            SemanticQuery = semanticQuery,
            Filters = new UnderstoodFilters
            {
                GenresInclude = include,
                GenresExclude = exclude,
                YearFrom = filters!.YearFrom,
                YearTo = filters.YearTo,
                MinRating = filters.MinRating,
                MaxPriceMinor = filters.MaxPriceMinor,
                Language = language,
            },
            SimilarToTitles = titles,
            DetectedLocale = locale is not null && Locales.All.Contains(locale) ? locale : null,
            Source = QueryUnderstandingSource.Llm,
        };
        error = "";
        return true;
    }

    private static List<string> Genres(IReadOnlyList<string>? values, string field, IReadOnlySet<string> genreSlugs, List<string> errors)
    {
        var slugs = (values ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var unknown = slugs.Where(s => !genreSlugs.Contains(s)).ToList();
        if (unknown.Count > 0)
        {
            errors.Add($"{field} has unknown genre slugs: {string.Join(", ", unknown)}. Allowed: {string.Join(", ", genreSlugs.Order(StringComparer.Ordinal))}.");
        }

        return slugs;
    }

    private static void Year(int? year, string field, List<string> errors)
    {
        if (year is < MinYear or > MaxYear)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{field} must be between {MinYear} and {MaxYear}."));
        }
    }

    /// <summary>Some models wrap JSON in a Markdown code fence even when asked not to.</summary>
    private static string StripCodeFence(string? text)
    {
        var trimmed = (text ?? "").Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstLineEnd = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd >= 0 && lastFence > firstLineEnd ? trimmed[(firstLineEnd + 1)..lastFence].Trim() : trimmed;
    }
}
