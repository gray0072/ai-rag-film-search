namespace FilmSearch.Core.Localization;

/// <summary>Locales of the store (spec 01, FR-CAT-4). English is the default and the fallback for missing texts.</summary>
public static class Locales
{
    public const string English = "en";
    public const string Russian = "ru";
    public const string Swedish = "sv";

    public const string Default = English;

    public static IReadOnlyList<string> All { get; } = [English, Russian, Swedish];

    /// <summary>
    /// Maps a language tag (<c>ru</c>, <c>sv-SE</c>, <c>EN</c>) to a supported locale, or to <see cref="Default"/>.
    /// </summary>
    public static string Normalize(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return Default;
        }

        var span = languageTag.AsSpan().Trim();
        var dash = span.IndexOfAny('-', '_');
        var language = dash >= 0 ? span[..dash] : span;

        foreach (var locale in All)
        {
            if (language.Equals(locale, StringComparison.OrdinalIgnoreCase))
            {
                return locale;
            }
        }

        return Default;
    }
}
