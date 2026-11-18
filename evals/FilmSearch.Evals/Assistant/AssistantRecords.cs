using FilmSearch.Core.Owners;
using FilmSearch.Evals.Datasets;

namespace FilmSearch.Evals.Assistant;

/// <summary>The owner of the current scope: every case run is a new owner with an empty cart and library (like a new visitor).</summary>
internal sealed class EvalOwnerContext : IOwnerContext
{
    public Guid OwnerId { get; set; }
}

/// <summary>A tool call of a turn as the UI saw it (<c>tool.started</c> / <c>tool.completed</c>).</summary>
public sealed record ToolCallRecord(string Name, string Args, bool Ok);

/// <summary>A movie card of a turn; TMDB id 0 when the movie has none.</summary>
public sealed record CardRecord(int TmdbId, string Title, int? Year, IReadOnlyList<string> GenreSlugs);

/// <summary>A cart line after a turn.</summary>
public sealed record CartLineRecord(int TmdbId, string Title, string Offer);

/// <summary>Result of one deterministic check of a turn.</summary>
public sealed record CheckResult(string Name, bool Passed, string? Detail = null);

/// <summary>Everything one turn produced: events, state after it, usage, and the check results.</summary>
public sealed record TurnRecord
{
    public required int Number { get; init; }

    public required string User { get; init; }

    public string Answer { get; set; } = "";

    public List<ToolCallRecord> Tools { get; } = [];

    public List<CardRecord> Cards { get; } = [];

    public bool CartUpdated { get; set; }

    public bool ConfirmationRequired { get; set; }

    /// <summary>Total of the checkout confirmation as the server computed it, e.g. <c>7.99 USD</c>.</summary>
    public string? ConfirmationTotal { get; set; }

    public List<CartLineRecord> CartAfter { get; } = [];

    /// <summary>Orders of the owner after the turn: the assistant alone must never create one (FR-AST-6).</summary>
    public int OrdersAfter { get; set; }

    /// <summary><c>code: message</c> of an <c>error</c> event.</summary>
    public string? Error { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    /// <summary>Time to the end of the answer as the API reports it (<c>message.completed</c>).</summary>
    public int? LatencyMs { get; set; }

    public int ModelCalls { get; set; }

    public bool IterationLimitHit { get; set; }

    /// <summary>Movie titles the grounding check found in the answer.</summary>
    public IReadOnlyList<string> Titles { get; set; } = [];

    /// <summary>Titles no tool returned and the user did not name: possible hallucinations.</summary>
    public IReadOnlyList<string> UnknownTitles { get; set; } = [];

    public string? DetectedLanguage { get; set; }

    public List<CheckResult> Checks { get; } = [];

    public bool Passed => Checks.All(c => c.Passed);
}

/// <summary>LLM judge scores of one case run (rubric 1–5, <c>assistant-judge</c> prompt).</summary>
public sealed record JudgeScores(int Relevance, int Explanations, int Tone, int Constraints, string Comment)
{
    public static IReadOnlyList<string> Criteria { get; } = ["relevance", "explanations", "tone", "constraints"];

    public double Mean => (Relevance + Explanations + Tone + Constraints) / 4.0;

    public int Get(string criterion) => criterion switch
    {
        "relevance" => Relevance,
        "explanations" => Explanations,
        "tone" => Tone,
        "constraints" => Constraints,
        _ => throw new ArgumentOutOfRangeException(nameof(criterion), criterion, null),
    };
}

/// <summary>One play of a case: its turns and the judge's verdict (null when the judge is off or failed).</summary>
public sealed record AssistantRun(int Repeat, IReadOnlyList<TurnRecord> Turns, JudgeScores? Judge, string? JudgeError, double WallMs)
{
    public bool Passed => Turns.All(t => t.Passed);

    /// <summary>The provider failed: not a quality signal, reported separately.</summary>
    public bool ProviderError => Turns.Any(t => t.Error?.StartsWith("providerUnavailable", StringComparison.Ordinal) == true);
}

public sealed record AssistantCaseResult(AssistantCase Case, IReadOnlyList<AssistantRun> Runs);
