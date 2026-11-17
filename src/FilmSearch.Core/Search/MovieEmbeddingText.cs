using System.Security.Cryptography;
using System.Text;
using FilmSearch.Core.Catalog;

namespace FilmSearch.Core.Search;

/// <summary>
/// Builds the embedding document of a movie (spec 05 §3): the text whose vector represents the movie in semantic
/// search. Deterministic: the same movie data always gives the same text. Field labels stay in English for every
/// locale; titles, genres, tagline and overview come in the locale of <see cref="MovieDetails"/> (English fallback).
/// <para>
/// What goes in matters more than any search parameter: the title and year anchor the movie, genres and keywords
/// describe its kind, people help queries such as "Nolan movie", and the overview carries the plot that most
/// "a movie about…" queries describe. Ratings and prices are left out: they are filters, not meaning.
/// </para>
/// Any change of the template must change <see cref="Version"/>: the version is part of <see cref="Hash"/>,
/// so every movie is re-embedded on the next reindex.
/// </summary>
public static class MovieEmbeddingText
{
    public const string Version = "movie-profile-v1";

    public const int MaxCast = 5;
    public const int MaxKeywords = 20;

    public static string Build(MovieDetails movie)
    {
        var text = new StringBuilder();
        text.Append("Title: ").Append(movie.Title);
        if (movie.Year is { } year)
        {
            text.Append(" (").Append(year).Append(')');
        }

        text.Append('\n');
        if (!string.Equals(movie.OriginalTitle, movie.Title, StringComparison.Ordinal))
        {
            Line(text, "Original title", movie.OriginalTitle);
        }

        Line(text, "Genres", string.Join(", ", movie.Genres.Select(g => g.Name)));
        Line(text, "Director", string.Join(", ", movie.Directors.Select(d => d.Name)));
        Line(text, "Cast", string.Join(", ", movie.Cast.Take(MaxCast).Select(c => c.Name)));
        Line(text, "Keywords", string.Join(", ", movie.Keywords.Take(MaxKeywords)));
        Line(text, "Tagline", movie.Tagline);
        Line(text, "Overview", movie.Overview);

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// SHA-256 of the template version and the exact text sent to the model (document prefix included), lower-case hex.
    /// A stored vector is current only while its hash equals the hash of the text built from the current movie data.
    /// </summary>
    public static string Hash(string embeddedText) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "\n" + embeddedText)));

    private static void Line(StringBuilder text, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // One field per line: newlines inside a value (some overviews have paragraphs) become spaces.
        var flat = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        text.Append(label).Append(": ").Append(flat).Append('\n');
    }
}
