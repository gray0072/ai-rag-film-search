using System.Reflection;
using System.Text.Json;
using FilmSearch.AI.Assistant.Tools;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using Microsoft.Extensions.AI;
using static FilmSearch.UnitTests.Assistant.ScriptedStreamingChatClient;

namespace FilmSearch.UnitTests.Assistant;

/// <summary>The I12 tools (spec 06 §3) and the sales flow of scenario 3 through the agent loop with a scripted model.</summary>
public sealed class AgentToolsTests
{
    private static readonly string[] ToolNames =
    [
        "search_movies", "get_movie_details", "find_similar_movies", "view_cart", "add_to_cart", "remove_from_cart", "request_checkout",
    ];

    private readonly AssistantHarness _harness = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sales_flow_search_then_buy_the_second_one_adds_to_cart_and_asks_for_confirmation_without_paying()
    {
        var beforeSunrise = AssistantHarness.Movie("Before Sunset", 2004);
        var amelie = AssistantHarness.Movie("Amélie", 2001);
        var notebook = AssistantHarness.Movie("The Notebook", 2004);
        _harness.Search.Results = [beforeSunrise, amelie, notebook];
        _harness.Cart.Know(beforeSunrise, amelie, notebook);
        var conversation = _harness.Store.Add("en", "local");

        // Turn 1: the model searches and lists three movies.
        _harness.Chat.Answer(Call("c1", SearchMoviesTool.ToolName, new() { ["query"] = "romantic evening, like Before Sunrise and La La Land" }));
        _harness.Chat.Answer(Text("For a cozy evening: **Before Sunset** (2004), **Amélie** (2001) and **The Notebook** (2004)."));
        await _harness.SendAsync(conversation, "Something for an evening with my wife, we liked Before Sunrise and La La Land");

        // Turn 2: "take the second one, buy it" → add_to_cart and request_checkout in one response (parallel calls), then the text.
        _harness.Chat.Answer(
            Call("c2", AddToCartTool.ToolName, new() { ["movieId"] = amelie.Id.ToString(), ["offerType"] = "buy" }),
            Call("c3", RequestCheckoutTool.ToolName, new()));
        _harness.Chat.Answer(Text("**Amélie** is in your cart. Check the order and press **Go to payment**."));

        var events = await _harness.SendAsync(conversation, "Take the second one, buy it");

        events.Select(e => e.EventType).ShouldBe(
        [
            "message.started",
            "tool.started", "tool.completed", "cart.updated",
            "tool.started", "tool.completed", "confirmation.required",
            "message.delta", "message.completed",
        ]);
        events.OfType<ToolCompletedEvent>().ShouldAllBe(e => e.Ok);

        // The cart line was added by the assistant, with the buy offer and the DB price.
        var line = _harness.Cart.Lines.ShouldHaveSingleItem();
        line.Movie.Id.ShouldBe(amelie.Id);
        line.Offer.Type.ShouldBe(OfferType.Buy);
        line.AddedBy.ShouldBe(CartItemSource.Assistant);
        events.OfType<CartUpdatedEvent>().Single().Cart.TotalMinor.ShouldBe(999);

        // The confirmation belongs to this answer and shows the server's summary; nothing was paid.
        var answerId = events.OfType<MessageStartedEvent>().Single().MessageId;
        var request = _harness.Confirmations.Requests.ShouldHaveSingleItem();
        request.ConversationId.ShouldBe(conversation.Id);
        request.MessageId.ShouldBe(answerId);
        request.ValidFor.ShouldBe(TimeSpan.FromMinutes(_harness.Options.ConfirmationTtlMinutes));
        var required = events.OfType<ConfirmationRequiredEvent>().Single();
        required.ConfirmationId.ShouldBe(request.View.Id);
        required.Kind.ShouldBe("checkout");
        required.Summary.TotalMinor.ShouldBe(999);
        required.Summary.Items.Single().MovieId.ShouldBe(amelie.Id);
        required.ExpiresAt.ShouldBe(_harness.Time.GetUtcNow().AddMinutes(_harness.Options.ConfirmationTtlMinutes));

        // The model read "awaiting_user_confirmation" with the total formatted by the server.
        var results = _harness.Chat.StreamRequests[^1].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
        results[^1].CallId.ShouldBe("c3");
        results[^1].Result!.ToString()!.ShouldContain(RequestCheckoutTool.AwaitingStatus);
        results[^1].Result!.ToString()!.ShouldContain("9.99 USD");

        // The second turn saw the first turn's tool result (ids) in its history, so "the second one" can be resolved.
        var history = _harness.Chat.StreamRequests[2].Messages;
        history.SelectMany(m => m.Contents).OfType<FunctionResultContent>().First().Result!.ToString()!.ShouldContain(amelie.Id.ToString());
    }

    [Fact]
    public async Task All_tools_are_declared_and_none_takes_an_owner_or_a_price()
    {
        _harness.Chat.Answer(Text("Hi!"));
        await _harness.SendAsync(_harness.Store.Add("en", "local"), "hello");

        var tools = _harness.Chat.StreamRequests.Single().Options!.Tools!.OfType<AIFunctionDeclaration>().ToList();
        tools.Select(t => t.Name).ShouldBe(ToolNames);
        foreach (var tool in tools)
        {
            tool.Description.ShouldNotBeNullOrWhiteSpace();
            var properties = tool.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ToList();
            properties.ShouldNotContain(p => p.Contains("owner") || p.Contains("user") || p.Contains("price") && tool.Name != "search_movies");
        }

        var addToCart = tools.Single(t => t.Name == "add_to_cart").JsonSchema;
        addToCart.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldBe(["movieId", "offerType"], ignoreOrder: true);
        addToCart.GetProperty("properties").GetProperty("offerType").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ShouldBe(["buy", "rent"]);
        tools.Single(t => t.Name == "request_checkout").JsonSchema.GetProperty("properties").EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public void No_tool_can_create_an_order_or_a_payment_session()
    {
        // FR-AST-6: the model alone can never pay. Tools get only these services; orders and payment sessions are
        // created by the confirmations endpoint after the user's click.
        var forbidden = new[] { typeof(ICheckoutService), typeof(IPaymentGateway) };
        var toolTypes = typeof(AssistantTool).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(AssistantTool)) && !t.IsAbstract).ToList();

        toolTypes.Count.ShouldBe(ToolNames.Length);
        foreach (var type in toolTypes)
        {
            var dependencies = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);
            dependencies.ShouldNotContain(d => forbidden.Contains(d), $"{type.Name} must not depend on order or payment services.");
        }

        // The checkout tool gets ICheckoutConfirmations, and a tool run never resolves a confirmation (the fake throws).
        typeof(RequestCheckoutTool).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ShouldContain(typeof(ICheckoutConfirmations));
    }

    [Theory]
    [InlineData(null, "buy", "`movieId` is required: " + AssistantTool<AddToCartArgs>.MovieIdHint + ".")]
    [InlineData("valid", null, "`offerType` is required: one of buy, rent.")]
    [InlineData("valid", "purchase", "`offerType` has an invalid value: one of buy, rent.")]
    [InlineData("Interstellar", "buy", "`movieId` has an invalid value: " + AssistantTool<AddToCartArgs>.MovieIdHint + ".")]
    public async Task Add_to_cart_arguments_are_checked_against_the_schema(string? movieId, string? offerType, string expected)
    {
        var movie = AssistantHarness.Movie("Moon", 2009);
        _harness.Cart.Know(movie);
        var arguments = new Dictionary<string, object?>();
        if (movieId is not null)
        {
            arguments["movieId"] = movieId == "valid" ? movie.Id.ToString() : movieId;
        }

        if (offerType is not null)
        {
            arguments["offerType"] = offerType;
        }

        var outcome = await Tool<AddToCartTool>().InvokeAsync(arguments, Context(), Ct);

        outcome.Ok.ShouldBeFalse();
        outcome.Result.ShouldContain(expected);
        outcome.Events.ShouldBeEmpty();
        _harness.Cart.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Add_to_cart_accepts_json_arguments_and_reports_refusals_of_the_cart()
    {
        var moon = AssistantHarness.Movie("Moon", 2009);
        var owned = AssistantHarness.Movie("Alien", 1979);
        _harness.Cart.Know(moon, owned);
        _harness.Cart.Owned.Add(owned.Id);
        var tool = Tool<AddToCartTool>();

        // Provider arguments are JsonElements; enum values in any case.
        var json = JsonSerializer.Deserialize<Dictionary<string, object?>>($$"""{"movieId":"{{moon.Id}}","offerType":"Rent"}""")!;
        var added = await tool.InvokeAsync(json, Context(), Ct);
        added.Ok.ShouldBeTrue();
        added.Result.ShouldContain("\"added\":\"Moon: rent for 48 h: 3.99 USD\"");
        added.Result.ShouldContain("\"total\":\"3.99 USD\"");
        added.Events.ShouldHaveSingleItem().ShouldBeOfType<CartUpdatedEvent>().Cart.Items.Single().OfferType.ShouldBe(OfferType.Rent);

        var alreadyOwned = await tool.InvokeAsync(new Dictionary<string, object?> { ["movieId"] = owned.Id.ToString(), ["offerType"] = "buy" }, Context(), Ct);
        alreadyOwned.Ok.ShouldBeFalse();
        alreadyOwned.Result.ShouldContain("already in your library");
        alreadyOwned.Events.ShouldBeEmpty();

        var unknown = await tool.InvokeAsync(new Dictionary<string, object?> { ["movieId"] = Guid.NewGuid().ToString(), ["offerType"] = "buy" }, Context(), Ct);
        unknown.Ok.ShouldBeFalse();
        unknown.Result.ShouldContain("Use a movie id from a tool result");
    }

    [Fact]
    public async Task Remove_from_cart_emits_cart_updated_only_when_something_was_removed()
    {
        var moon = AssistantHarness.Movie("Moon", 2009);
        _harness.Cart.Know(moon);
        await _harness.Cart.AddItemAsync(moon.Id, OfferType.Buy, CartItemSource.User, "en", Ct);
        var tool = Tool<RemoveFromCartTool>();

        var notInCart = await tool.InvokeAsync(new Dictionary<string, object?> { ["movieId"] = Guid.NewGuid().ToString() }, Context(), Ct);
        notInCart.Ok.ShouldBeTrue();
        notInCart.Result.ShouldContain("not in the cart");
        notInCart.Events.ShouldBeEmpty();

        var removed = await tool.InvokeAsync(new Dictionary<string, object?> { ["movieId"] = moon.Id.ToString() }, Context(), Ct);
        removed.Ok.ShouldBeTrue();
        removed.Result.ShouldContain("\"removed\":\"Moon\"");
        removed.Events.ShouldHaveSingleItem().ShouldBeOfType<CartUpdatedEvent>().Cart.Items.ShouldBeEmpty();
        _harness.Cart.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task View_cart_shows_lines_and_the_total_computed_by_the_server()
    {
        var moon = AssistantHarness.Movie("Moon", 2009);
        var alien = AssistantHarness.Movie("Alien", 1979);
        _harness.Cart.Know(moon, alien);
        await _harness.Cart.AddItemAsync(moon.Id, OfferType.Buy, CartItemSource.User, "en", Ct);
        await _harness.Cart.AddItemAsync(alien.Id, OfferType.Rent, CartItemSource.Assistant, "en", Ct);

        var outcome = await Tool<ViewCartTool>().InvokeAsync(null, Context(), Ct);

        outcome.Ok.ShouldBeTrue();
        using var result = JsonDocument.Parse(outcome.Result);
        result.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("offer").GetString()).ShouldBe(["buy: 9.99 USD", "rent for 48 h: 3.99 USD"]);
        result.RootElement.GetProperty("items")[0].GetProperty("movieId").GetGuid().ShouldBe(moon.Id);
        result.RootElement.GetProperty("total").GetString().ShouldBe("13.98 USD");
        outcome.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Request_checkout_with_an_empty_cart_is_an_error_and_asks_for_nothing()
    {
        var outcome = await Tool<RequestCheckoutTool>().InvokeAsync(new Dictionary<string, object?>(), Context(), Ct);

        outcome.Ok.ShouldBeFalse();
        outcome.Result.ShouldContain("The cart is empty");
        outcome.Events.ShouldBeEmpty();
        _harness.Confirmations.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Movie_details_are_compact_and_unknown_ids_are_errors()
    {
        var id = Guid.CreateVersion7();
        _harness.Catalog.Details[id] = new MovieDetails(
            id, 1, "tt1", "Солярис", "Солярис", "ru", new DateOnly(1972, 3, 20), 1972, 167, new string('о', 1500), "A tagline", "/p.jpg", null, 8.1m, 900,
            ["space", "memory"], [new GenreItem(1, "science-fiction", "Фантастика")], [new PersonItem(Guid.NewGuid(), "Андрей Тарковский")], [],
            [.. Enumerable.Range(1, 10).Select(i => new CastItem(Guid.NewGuid(), $"Actor {i}", i == 1 ? "Kris Kelvin" : null))],
            [new OfferItem(Guid.NewGuid(), OfferType.Buy, 1299, "USD", null)]);
        var tool = Tool<GetMovieDetailsTool>();

        var outcome = await tool.InvokeAsync(new Dictionary<string, object?> { ["movieId"] = id.ToString() }, Context("ru"), Ct);

        outcome.Ok.ShouldBeTrue();
        outcome.Movies.ShouldBeEmpty();
        using var result = JsonDocument.Parse(outcome.Result);
        var root = result.RootElement;
        root.GetProperty("title").GetString().ShouldBe("Солярис");
        root.TryGetProperty("originalTitle", out _).ShouldBeFalse(); // same as the title
        root.GetProperty("directors")[0].GetString().ShouldBe("Андрей Тарковский");
        root.GetProperty("cast").GetArrayLength().ShouldBe(GetMovieDetailsTool.MaxCast);
        root.GetProperty("cast")[0].GetString().ShouldBe("Actor 1 as Kris Kelvin");
        root.GetProperty("overview").GetString()!.Length.ShouldBeLessThanOrEqualTo(GetMovieDetailsTool.MaxOverviewLength);
        root.GetProperty("offers")[0].GetString().ShouldBe("buy: 12.99 USD");

        var unknown = await tool.InvokeAsync(new Dictionary<string, object?> { ["movieId"] = Guid.NewGuid().ToString() }, Context(), Ct);
        unknown.Ok.ShouldBeFalse();
        unknown.Result.ShouldContain("was not found");
    }

    [Fact]
    public async Task Similar_movies_are_cards_like_search_results()
    {
        var solaris = AssistantHarness.Movie("Solaris", 1972);
        var stalker = AssistantHarness.Movie("Stalker", 1979);
        var lonely = AssistantHarness.Movie("Lonely", 2020);
        _harness.Search.Similar[solaris.Id] = [stalker];
        _harness.Search.Similar[lonely.Id] = [];
        var tool = Tool<FindSimilarMoviesTool>();

        var outcome = await tool.InvokeAsync(new Dictionary<string, object?> { ["movieId"] = solaris.Id.ToString(), ["limit"] = 3 }, Context(), Ct);
        outcome.Ok.ShouldBeTrue();
        outcome.Movies.ShouldHaveSingleItem().Id.ShouldBe(stalker.Id);
        outcome.Result.ShouldContain("\"title\":\"Stalker\"");

        var none = await tool.InvokeAsync(new Dictionary<string, object?> { ["movieId"] = lonely.Id.ToString() }, Context(), Ct);
        none.Ok.ShouldBeTrue();
        none.Result.ShouldContain("Use search_movies");

        var unknown = await tool.InvokeAsync(new Dictionary<string, object?> { ["movieId"] = Guid.NewGuid().ToString() }, Context(), Ct);
        unknown.Ok.ShouldBeFalse();
    }

    [Fact]
    public async Task The_answer_is_checked_for_movies_that_no_tool_returned()
    {
        var measurements = new List<(long Value, bool Grounded)>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "assistant.grounding.titles" && _harness.MeterFactory.Owns(instrument.Meter))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "grounded")
                {
                    measurements.Add((value, (bool)tag.Value!));
                }
            }
        });
        listener.Start();

        _harness.Search.Results = [AssistantHarness.Movie("Interstellar")];
        _harness.Chat.Answer(Call("c1", SearchMoviesTool.ToolName, new() { ["query"] = "space" }));
        _harness.Chat.Answer(Text("Try **Interstellar** (2014) or **Gravity** (2013). Since you liked **La La Land**: **Total:** nothing yet. Press **Go to payment** later."));

        await _harness.SendAsync(_harness.Store.Add("en", "local"), "Space movies, I liked La La Land");

        // Interstellar (tool), La La Land (user) and the button label (system prompt) are grounded; Gravity is not; "Total:" is a label.
        measurements.ShouldBe([(3, true), (1, false)], ignoreOrder: true);
    }

    [Fact]
    public async Task Answers_use_prompt_v3_by_default_and_v1_can_be_pinned()
    {
        _harness.Chat.Answer(Text("Hi!"));
        var conversation = _harness.Store.Add("en", "local");
        await _harness.SendAsync(conversation, "hello");
        _harness.Store.Messages.Single(m => m.Role == MessageRole.Assistant).PromptVersion.ShouldBe("assistant-system.v3");
        _harness.Chat.StreamRequests[0].Messages[0].Text.ShouldContain("request_checkout");

        // The payment button label of every UI locale is in the prompt, so the grounding check knows it (I14).
        _harness.Chat.StreamRequests[0].Messages[0].Text.ShouldContain("**Перейти к оплате**");
        _harness.Chat.StreamRequests[0].Messages[0].Text.ShouldContain("**Gå till betalning**");

        _harness.Options.PromptVersion = 1;
        await _harness.SendAsync(conversation, "hello again");
        _harness.Store.Messages.Last(m => m.Role == MessageRole.Assistant).PromptVersion.ShouldBe("assistant-system.v1");
    }

    private T Tool<T>()
        where T : AssistantTool => _harness.CreateTools().OfType<T>().Single();

    private static ToolContext Context(string locale = "en") => new(Guid.NewGuid(), locale, Guid.NewGuid());
}
