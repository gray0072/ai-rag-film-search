using FilmSearch.Core.Search;

namespace FilmSearch.Evals;

/// <summary>A search setup compared in the search suite: a retrieval mode, with or without query understanding.</summary>
public sealed record SearchModeOption(string Name, SearchMode Mode, bool Understand)
{
    public static SearchModeOption Keyword { get; } = new("keyword", SearchMode.Keyword, false);

    public static SearchModeOption Semantic { get; } = new("semantic", SearchMode.Semantic, false);

    public static SearchModeOption Hybrid { get; } = new("hybrid", SearchMode.Hybrid, false);

    public static SearchModeOption HybridQu { get; } = new("hybrid+qu", SearchMode.Hybrid, true);

    public static IReadOnlyList<SearchModeOption> All { get; } = [Keyword, Semantic, Hybrid, HybridQu];

    public static IReadOnlyList<SearchModeOption> ParseList(string list)
    {
        var modes = new List<SearchModeOption>();
        foreach (var name in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var mode = All.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown mode '{name}'. Modes: {string.Join(", ", All.Select(m => m.Name))}.");
            if (!modes.Contains(mode))
            {
                modes.Add(mode);
            }
        }

        return modes.Count > 0 ? modes : throw new ArgumentException("--modes is empty.");
    }
}
