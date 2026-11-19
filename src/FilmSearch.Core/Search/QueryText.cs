using System.Text;

namespace FilmSearch.Core.Search;

/// <summary>Search query text helpers.</summary>
public static class QueryText
{
    /// <summary>
    /// Cache key form of a query: lower case, runs of whitespace as one space, ё as е. These do not change what the user
    /// is looking for, so "Крёстный  Отец" and "крестный отец" share cached results (query understanding, query vectors).
    /// </summary>
    public static string Normalize(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var builder = new StringBuilder(query.Length);
        foreach (var part in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(part.ToLowerInvariant().Replace('ё', 'е'));
        }

        return builder.ToString();
    }
}
