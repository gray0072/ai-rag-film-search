using FilmSearch.Core.Search;

namespace FilmSearch.Api.Modules.Search;

/// <summary>
/// Defaults of the search page (I22, section <c>Search:Defaults</c>): the mode a search uses when the request names none,
/// and whether the web app turns query understanding on before the visitor changes it.
/// </summary>
public sealed class SearchDefaultsOptions
{
    public const string SectionName = "Search:Defaults";

    /// <summary>Mode of <c>POST /api/search</c> without <c>mode</c>, and the mode the search page starts with.</summary>
    public SearchMode Mode { get; set; } = SearchMode.Hybrid;

    /// <summary>Whether the search page asks for query understanding by default. The API itself only understands when the request says so.</summary>
    public bool Understand { get; set; } = true;
}

/// <summary>Defaults of the search page, from <c>Search:Defaults</c>.</summary>
/// <param name="Mode">Search mode the page starts with; also the API default when a request has no mode.</param>
/// <param name="Understand">Whether "Understand the query" starts switched on.</param>
public sealed record SearchSettings(SearchMode Mode, bool Understand);
