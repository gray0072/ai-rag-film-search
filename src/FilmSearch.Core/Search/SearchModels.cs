using FilmSearch.Core.Catalog;

namespace FilmSearch.Core.Search;

public enum SearchMode
{
    /// <summary>PostgreSQL full-text search over titles, people, keywords and overviews (spec 05 §2).</summary>
    Keyword,

    /// <summary>Vector similarity over movie embeddings (spec 05 §3, from I06).</summary>
    Semantic,

    /// <summary>Keyword and semantic lists fused with RRF (spec 05 §4, from I07).</summary>
    Hybrid,
}

/// <param name="Text">The user's query in any supported language (en, ru, sv).</param>
/// <param name="Mode">Retrieval mode.</param>
/// <param name="Filter">Catalog filters, applied inside the retrieval queries.</param>
/// <param name="Locale">Locale of the returned movie texts; it does not limit the languages the query matches.</param>
/// <param name="Page">Page number, starting at 1.</param>
/// <param name="PageSize">Items per page.</param>
/// <param name="Debug">Return per-item retrieval scores (<see cref="SearchDebug"/>).</param>
public sealed record SearchQuery(
    string Text,
    SearchMode Mode,
    MovieFilter Filter,
    string Locale,
    int Page = 1,
    int PageSize = 20,
    bool Debug = false);

public sealed record SearchResult(IReadOnlyList<SearchItem> Items, int Page, int PageSize, int Total);

/// <param name="Movie">The movie in the requested locale.</param>
/// <param name="Score">Score of the mode that produced the list: keyword rank score, similarity or fused score. Higher is better.</param>
/// <param name="Debug">Retrieval details, when requested.</param>
public sealed record SearchItem(MovieSummary Movie, double Score, SearchDebug? Debug);

/// <summary>
/// Retrieval details of one item (spec 05 §4). Fields of retrievers that did not run are null.
/// Ranks start at 1 and are positions in that retriever's list.
/// </summary>
public sealed record SearchDebug
{
    public int? KeywordRank { get; init; }

    /// <summary><c>ts_rank_cd</c> over the best-matching text search config, normalized to 0–1.</summary>
    public double? KeywordScore { get; init; }

    /// <summary>
    /// <c>all</c>: every query term matched (<c>websearch_to_tsquery</c>). <c>any</c>: no movie matched all terms,
    /// so the query was relaxed to match any term.
    /// </summary>
    public string? KeywordMatch { get; init; }

    public int? VectorRank { get; init; }

    public double? Similarity { get; init; }

    public double? FusedScore { get; init; }
}
