using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using static FilmSearch.IntegrationTests.Infrastructure.CommerceFixtureBase;

namespace FilmSearch.IntegrationTests;

/// <summary>Own database with the commerce test movies; the model is scripted.</summary>
public sealed class AssistantCheckoutFixture(PostgresFixture postgres) : CommerceFixtureBase(postgres);

/// <summary>
/// The assistant sells (I12, FR-AST-5, FR-AST-6, scenario 3): cart tools over the real Commerce module, the checkout
/// confirmation stored by <c>request_checkout</c>, and the confirmations endpoint that alone creates the order and the
/// payment session. Every test scripts its own user message; every client is a new owner.
/// </summary>
public sealed class AssistantCheckoutApiTests(AssistantCheckoutFixture fixture) : IClassFixture<AssistantCheckoutFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid MovieOne => fixture.MovieIds[0];

    private Guid MovieTwo => fixture.MovieIds[1];

    [Fact]
    public async Task Scenario_3_the_assistant_fills_the_cart_and_only_the_users_click_creates_the_order_and_the_payment()
    {
        using var client = fixture.CreateClient();
        var conversationId = await CreateConversationAsync(client);

        var events = await AskToBuyAsync(client, conversationId, [MovieTwo]);

        events.Select(e => e.Name).ShouldBe(
        [
            "message.started",
            "tool.started", "tool.completed", "cart.updated",
            "tool.started", "tool.completed", "confirmation.required",
            "message.delta", "message.completed",
        ]);
        var cartEvent = events.Single(e => e.Name == "cart.updated").Data.GetProperty("cart").Deserialize<CartView>(Json)!;
        cartEvent.Items.Single().MovieId.ShouldBe(MovieTwo);
        var required = events.Single(e => e.Name == "confirmation.required").Data;
        var confirmationId = required.GetProperty("confirmationId").GetGuid();
        required.GetProperty("kind").GetString().ShouldBe("checkout");
        required.GetProperty("summary").GetProperty("totalMinor").GetInt64().ShouldBe(cartEvent.TotalMinor);
        required.GetProperty("expiresAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        // The cart line is the assistant's; no order and no payment exist until the user answers.
        var cart = (await client.GetFromJsonAsync<CartView>("/api/cart", Json, Ct))!;
        cart.Items.Single().AddedBy.ShouldBe(CartItemSource.Assistant);
        (await client.GetFromJsonAsync<List<OrderView>>("/api/orders", Json, Ct))!.ShouldBeEmpty();
        (await fixture.QueryAsync(db => db.Set<Payment>().CountAsync(p => db.Set<Order>().Any(o => o.Id == p.OrderId && o.ConversationId == conversationId), Ct))).ShouldBe(0);

        // The click.
        var result = await ApproveAsync(client, conversationId, confirmationId);
        result.Status.ShouldBe(ConfirmationStatus.Approved);
        result.CheckoutUrl.ShouldNotBeNullOrEmpty();
        var order = await GetOrderAsync(client, result.OrderId!.Value);
        order.Source.ShouldBe(OrderSource.Assistant);
        order.Status.ShouldBe(OrderStatus.PendingPayment);
        order.TotalMinor.ShouldBe(cart.TotalMinor);
        order.Items.Single().MovieId.ShouldBe(MovieTwo);

        // A double click returns the same session.
        var again = await ApproveAsync(client, conversationId, confirmationId);
        (again.OrderId, again.CheckoutUrl).ShouldBe((result.OrderId, result.CheckoutUrl));
        (await client.GetFromJsonAsync<List<OrderView>>("/api/orders", Json, Ct))!.Count.ShouldBe(1);

        // After a reload the answer shows the confirmation with its state.
        var view = (await client.GetFromJsonAsync<ConversationView>($"/api/chat/conversations/{conversationId}", Json, Ct))!;
        var button = view.Messages.Single(m => m.Role == MessageRole.Assistant).Confirmation.ShouldNotBeNull();
        button.Id.ShouldBe(confirmationId);
        button.Status.ShouldBe(ConfirmationStatus.Approved);
        button.OrderId.ShouldBe(result.OrderId);

        // Pay on the provider page; the webhook marks the order paid and the movie appears in the library.
        using var paid = await PressAsync(client, result.CheckoutUrl!, "pay");
        paid.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        await WaitForStatusAsync(client, order.Id, OrderStatus.Paid);
        (await client.GetFromJsonAsync<List<LibraryItem>>("/api/library", Json, Ct))!.ShouldContain(i => i.MovieId == MovieTwo);
    }

    [Fact]
    public async Task A_confirmation_for_a_changed_cart_is_rejected()
    {
        using var client = fixture.CreateClient();
        var conversationId = await CreateConversationAsync(client);
        var confirmationId = ConfirmationId(await AskToBuyAsync(client, conversationId, [MovieOne]));

        await AddToCartAsync(client, MovieTwo, OfferType.Rent);
        using (var changed = await AnswerAsync(client, conversationId, confirmationId, approved: true))
        {
            await ShouldBeProblemAsync(changed, HttpStatusCode.Conflict, "cartChanged");
        }

        (await client.GetFromJsonAsync<List<OrderView>>("/api/orders", Json, Ct))!.ShouldBeEmpty();

        // Switching buy to rent changes the price: also rejected.
        using var removed = await client.DeleteAsync($"/api/cart/items/{MovieTwo}", Ct);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);

        await AddToCartAsync(client, MovieOne, OfferType.Rent);
        using (var offerChanged = await AnswerAsync(client, conversationId, confirmationId, approved: true))
        {
            await ShouldBeProblemAsync(offerChanged, HttpStatusCode.Conflict, "cartChanged");
        }

        // The cart the user was shown again: the confirmation works.
        await AddToCartAsync(client, MovieOne, OfferType.Buy);
        (await ApproveAsync(client, conversationId, confirmationId)).Status.ShouldBe(ConfirmationStatus.Approved);
    }

    [Fact]
    public async Task Another_owner_cannot_answer_a_confirmation()
    {
        using var owner = fixture.CreateClient();
        using var stranger = fixture.CreateClient();
        var conversationId = await CreateConversationAsync(owner);
        var confirmationId = ConfirmationId(await AskToBuyAsync(owner, conversationId, [MovieOne]));
        var strangersConversation = await CreateConversationAsync(stranger);

        using (var foreignConversation = await AnswerAsync(stranger, conversationId, confirmationId, approved: true))
        {
            foreignConversation.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var ownConversation = await AnswerAsync(stranger, strangersConversation, confirmationId, approved: true))
        {
            await ShouldBeProblemAsync(ownConversation, HttpStatusCode.NotFound, "confirmationNotFound");
        }

        (await stranger.GetFromJsonAsync<List<OrderView>>("/api/orders", Json, Ct))!.ShouldBeEmpty();
        (await owner.GetFromJsonAsync<List<OrderView>>("/api/orders", Json, Ct))!.ShouldBeEmpty();
        (await ApproveAsync(owner, conversationId, confirmationId)).Status.ShouldBe(ConfirmationStatus.Approved);
    }

    [Fact]
    public async Task Declined_superseded_and_expired_confirmations_cannot_be_approved()
    {
        using var client = fixture.CreateClient();
        var conversationId = await CreateConversationAsync(client);

        // Declined (twice is fine), then approve → 409.
        var declined = ConfirmationId(await AskToBuyAsync(client, conversationId, [MovieOne]));
        using (var response = await AnswerAsync(client, conversationId, declined, approved: false))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadFromJsonAsync<ConfirmationResult>(Json, Ct))!.ShouldBe(new ConfirmationResult(ConfirmationStatus.Declined, null, null, null));
        }

        using (var twice = await AnswerAsync(client, conversationId, declined, approved: false))
        {
            twice.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var response = await AnswerAsync(client, conversationId, declined, approved: true))
        {
            await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, "confirmationNotPending");
        }

        // A newer request supersedes the older one.
        var older = ConfirmationId(await AskToBuyAsync(client, conversationId, []));
        var newer = ConfirmationId(await AskToBuyAsync(client, conversationId, []));
        using (var response = await AnswerAsync(client, conversationId, older, approved: true))
        {
            await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, "confirmationNotPending");
        }

        // Past its expiry time.
        await fixture.QueryAsync(db => db.Set<CheckoutConfirmation>()
            .Where(c => c.Id == newer)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        using (var response = await AnswerAsync(client, conversationId, newer, approved: true))
        {
            await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, "confirmationExpired");
        }

        var view = (await client.GetFromJsonAsync<ConversationView>($"/api/chat/conversations/{conversationId}", Json, Ct))!;
        view.Messages.Where(m => m.Confirmation is not null).Select(m => m.Confirmation!.Status)
            .ShouldBe([ConfirmationStatus.Declined, ConfirmationStatus.Superseded, ConfirmationStatus.Expired]);
        (await client.GetFromJsonAsync<List<OrderView>>("/api/orders", Json, Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Checkout_with_an_empty_cart_asks_for_nothing()
    {
        using var client = fixture.CreateClient();
        var conversationId = await CreateConversationAsync(client);

        var events = await AskToBuyAsync(client, conversationId, []);

        events.Select(e => e.Name).ShouldNotContain("confirmation.required");
        events.Single(e => e.Name == "tool.completed").Data.GetProperty("ok").GetBoolean().ShouldBeFalse();
        (await fixture.QueryAsync(db => db.Set<CheckoutConfirmation>().CountAsync(c => c.ConversationId == conversationId, Ct))).ShouldBe(0);
    }

    /// <summary>
    /// A turn in which the scripted model adds the movies (buy) and calls <c>request_checkout</c> in one response, then
    /// answers with text. The user message is unique, so scripts of parallel tests do not mix.
    /// </summary>
    private async Task<List<(string Name, JsonElement Data)>> AskToBuyAsync(HttpClient client, Guid conversationId, Guid[] movieIds)
    {
        var text = $"Buy it, please ({Guid.NewGuid():N})";
        AIContent[] calls =
        [
            .. movieIds.Select((id, i) => new FunctionCallContent($"add-{i}", "add_to_cart", new Dictionary<string, object?> { ["movieId"] = id.ToString(), ["offerType"] = "buy" })),
            new FunctionCallContent("checkout", "request_checkout", new Dictionary<string, object?>()),
        ];
        fixture.Factory.Chat.ScriptStream(text, calls, [new TextContent("Check the order and press **Go to payment**.")]);

        using var response = await client.PostAsJsonAsync($"/api/chat/conversations/{conversationId}/messages", new { text }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var events = AssistantApiTests.ParseEvents(await response.Content.ReadAsStringAsync(Ct));
        events[^1].Name.ShouldBe("message.completed");
        return events;
    }

    private static Guid ConfirmationId(List<(string Name, JsonElement Data)> events) =>
        events.Single(e => e.Name == "confirmation.required").Data.GetProperty("confirmationId").GetGuid();

    private static async Task<Guid> CreateConversationAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/chat/conversations", content: null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ConversationListItem>(Json, Ct))!.Id;
    }

    private static Task<HttpResponseMessage> AnswerAsync(HttpClient client, Guid conversationId, Guid confirmationId, bool approved) =>
        client.PostAsJsonAsync($"/api/chat/conversations/{conversationId}/confirmations/{confirmationId}", new { approved }, Ct);

    private static async Task<ConfirmationResult> ApproveAsync(HttpClient client, Guid conversationId, Guid confirmationId)
    {
        using var response = await AnswerAsync(client, conversationId, confirmationId, approved: true);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ConfirmationResult>(Json, Ct))!;
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Extensions["code"]!.ToString().ShouldBe(code);
    }
}
