namespace FilmSearch.Evals.Datasets;

/// <summary>
/// One line of <c>assistant.scenarios.jsonl</c> or <c>assistant.adversarial.jsonl</c> (spec 09 §2): a conversation of
/// user turns played through the real agent, with deterministic expectations per turn and notes for the LLM judge.
/// Movies are TMDB ids, as in the search datasets.
/// </summary>
/// <param name="Id">Unique case id, e.g. <c>en-buy-second</c>.</param>
/// <param name="Locale">Locale of the conversation (tool results and cards) and the expected answer language.</param>
/// <param name="Turns">User messages in order, each with what the answer to it must and must not do.</param>
/// <param name="Tags">Case types (<see cref="AssistantTags"/>), for the breakdown in the report.</param>
/// <param name="Description">What the case tests, for people reading the report.</param>
/// <param name="Judge">What a good conversation does: given to the judge with the rubric. Null = rubric only.</param>
/// <param name="Setup">State before the first turn (the owner's cart).</param>
public sealed record AssistantCase(
    string Id,
    string Locale,
    IReadOnlyList<AssistantTurn> Turns,
    IReadOnlyList<string> Tags,
    string? Description = null,
    string? Judge = null,
    AssistantSetup? Setup = null);

/// <summary>A user message and the expectations for the answer to it.</summary>
public sealed record AssistantTurn(string User, TurnExpectation? Expect = null);

/// <summary>The owner's state before the conversation starts.</summary>
/// <param name="Cart">Lines put into the cart by the user (not by the assistant).</param>
/// <param name="StoppedMessages">
/// User messages already in the conversation whose answers were stopped before they were stored (the user pressed
/// Stop): what the API stores in that case, so the next turn sees the same history as in production.
/// </param>
public sealed record AssistantSetup(IReadOnlyList<SetupCartLine>? Cart = null, IReadOnlyList<string>? StoppedMessages = null);

/// <param name="TmdbId">The movie.</param>
/// <param name="Offer"><c>buy</c> or <c>rent</c>.</param>
public sealed record SetupCartLine(int TmdbId, string Offer);

/// <summary>
/// Deterministic expectations for one answer (spec 09 §3). Every field is optional; a missing field is not checked. The
/// checks that apply to every answer (no error, grounding, answer language, no order created by the assistant, no
/// iteration limit) need no field.
/// </summary>
public sealed record TurnExpectation
{
    /// <summary>Tools that must be called in this turn (each at least once).</summary>
    public IReadOnlyList<string> ToolsCalled { get; init; } = [];

    /// <summary>Tools that must not be called; <c>*</c> = no tool at all.</summary>
    public IReadOnlyList<string> ToolsNotCalled { get; init; } = [];

    /// <summary>At least one of these movies is shown as a card in this turn.</summary>
    public IReadOnlyList<int> MoviesAnyOf { get; init; } = [];

    /// <summary>None of the cards of this turn has one of these genre slugs (a "no horror" constraint).</summary>
    public IReadOnlyList<string> CardsExcludeGenres { get; init; } = [];

    /// <summary>Every card of this turn is released in or after this year.</summary>
    public int? CardsYearFrom { get; init; }

    /// <summary>Every card of this turn is released in or before this year.</summary>
    public int? CardsYearTo { get; init; }

    /// <summary>At least this many cards in this turn: the answer recommends movies instead of asking or refusing.</summary>
    public int? MinCards { get; init; }

    /// <summary>At most this many cards in this turn (0 for off-topic requests).</summary>
    public int? MaxCards { get; init; }

    /// <summary>The cart after the turn.</summary>
    public CartExpectation? Cart { get; init; }

    /// <summary>True: the answer asks for checkout confirmation (<c>confirmation.required</c>); false: it must not.</summary>
    public bool? Confirmation { get; init; }

    /// <summary>Expected answer language; null = the case locale; <c>any</c> = not checked.</summary>
    public string? Language { get; init; }

    /// <summary>Regular expressions (case-insensitive) the answer text must match, each of them.</summary>
    public IReadOnlyList<string> AnswerMatches { get; init; } = [];

    /// <summary>Regular expressions (case-insensitive) the answer text must not match.</summary>
    public IReadOnlyList<string> AnswerNotMatches { get; init; } = [];

    /// <summary>At most this many model calls in the turn.</summary>
    public int? MaxModelCalls { get; init; }
}

/// <summary>The cart after a turn.</summary>
/// <param name="Contains">Movies that must be in the cart.</param>
/// <param name="NotContains">Movies that must not be in the cart.</param>
/// <param name="Offer"><c>buy</c> or <c>rent</c>: the offer of every line in <paramref name="Contains"/>.</param>
/// <param name="Count">Exact number of lines.</param>
/// <param name="Unchanged">No cart tool changed the cart in this turn (no <c>cart.updated</c> event).</param>
public sealed record CartExpectation(
    IReadOnlyList<int>? Contains = null,
    IReadOnlyList<int>? NotContains = null,
    string? Offer = null,
    int? Count = null,
    bool? Unchanged = null);

/// <summary>Allowed tags of assistant cases.</summary>
public static class AssistantTags
{
    public const string Recommend = "recommend";
    public const string Constraint = "constraint";
    public const string Similar = "similar";
    public const string Details = "details";
    public const string Clarify = "clarify";
    public const string NoResults = "no-results";
    public const string FollowUp = "follow-up";
    public const string Cart = "cart";
    public const string Checkout = "checkout";
    public const string BuyOrRent = "buy-or-rent";
    public const string Language = "language";

    /// <summary>Taken from a real conversation that went wrong (chat logs, thumbs-down).</summary>
    public const string Real = "real";

    // Adversarial.
    public const string Injection = "injection";
    public const string OffTopic = "off-topic";
    public const string Price = "price";
    public const string Payment = "payment";
    public const string LongInput = "long-input";
    public const string Hallucination = "hallucination";
    public const string Privacy = "privacy";

    public static IReadOnlyList<string> All { get; } =
    [
        Recommend, Constraint, Similar, Details, Clarify, NoResults, FollowUp, Cart, Checkout, BuyOrRent, Language, Real,
        Injection, OffTopic, Price, Payment, LongInput, Hallucination, Privacy,
    ];
}

/// <summary>Names of the assistant's tools (I11, I12), as the model calls them.</summary>
public static class AssistantToolNames
{
    public const string SearchMovies = "search_movies";
    public const string GetMovieDetails = "get_movie_details";
    public const string FindSimilarMovies = "find_similar_movies";
    public const string ViewCart = "view_cart";
    public const string AddToCart = "add_to_cart";
    public const string RemoveFromCart = "remove_from_cart";
    public const string RequestCheckout = "request_checkout";

    /// <summary>In <see cref="TurnExpectation.ToolsNotCalled"/>: no tool at all.</summary>
    public const string Any = "*";

    public static IReadOnlyList<string> All { get; } =
        [SearchMovies, GetMovieDetails, FindSimilarMovies, ViewCart, AddToCart, RemoveFromCart, RequestCheckout];
}
