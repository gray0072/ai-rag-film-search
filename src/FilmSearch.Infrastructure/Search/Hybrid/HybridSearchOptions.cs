namespace FilmSearch.Infrastructure.Search.Hybrid;

/// <summary>How the movies of "movies like X" (query understanding) change the vector part of the search.</summary>
public enum SimilarTitlesStrategy
{
    /// <summary>The query vector is averaged with the vectors of the named movies (each side normalized, equal weight).</summary>
    AverageVector,

    /// <summary>The neighbours of each named movie are an extra list in the rank fusion (hybrid mode).</summary>
    ExtraList,
}

/// <summary>Configuration section <c>Search:Hybrid</c> (spec 05 §4). Tuned with evaluations (I08).</summary>
public sealed class HybridSearchOptions
{
    public const string SectionName = "Search:Hybrid";

    /// <summary>
    /// RRF constant k: larger values flatten the difference between top and lower ranks. 20 instead of the usual 60:
    /// tuned on manual queries in I07 (report in evals/reports), to be checked with the I08 golden set.
    /// </summary>
    public int RrfK { get; set; } = 20;

    /// <summary>Weight of the keyword list when every query word matched (precise: titles, names).</summary>
    public double KeywordWeight { get; set; } = 1.0;

    /// <summary>
    /// Weight of the keyword list when no movie matched all words and the query was relaxed to any word. Such a list is
    /// mostly noise for descriptions ("a man discovers that his life is a TV show" matches every overview with "man" or
    /// "life"); with full weight it pushed the right semantic hits down (I07 experiment).
    /// </summary>
    public double RelaxedKeywordWeight { get; set; } = 0.1;

    /// <summary>Weight of the semantic (query vector) list in the fusion.</summary>
    public double SemanticWeight { get; set; } = 1.0;

    /// <summary>Candidates taken from each retriever before fusion; the hybrid result list is at most the union of them.</summary>
    public int CandidatesPerList { get; set; } = 50;

    public SimilarTitlesStrategy SimilarTitles { get; set; } = SimilarTitlesStrategy.AverageVector;

    /// <summary>Weight of each extra list of <see cref="SimilarTitlesStrategy.ExtraList"/>.</summary>
    public double SimilarTitleWeight { get; set; } = 1.0;

    /// <summary>At most this many titles of "movies like X" are resolved to movies.</summary>
    public int MaxSimilarTitles { get; set; } = 3;
}
