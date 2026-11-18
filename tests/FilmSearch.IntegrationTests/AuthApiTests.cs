using System.Net;
using System.Net.Http.Json;
using FilmSearch.Api.Modules.Identity;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using static FilmSearch.IntegrationTests.Infrastructure.AuthTestExtensions;
using static FilmSearch.IntegrationTests.Infrastructure.CommerceFixtureBase;

namespace FilmSearch.IntegrationTests;

/// <summary>Accounts (I17, FR-USR-2, FR-USR-3): register, login, logout, the merge of anonymous data, owner checks between users, the admin role.</summary>
public sealed class AuthApiTests(CommerceApiFixture fixture) : IClassFixture<CommerceApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid MovieOne => fixture.MovieIds[0];

    private Guid MovieTwo => fixture.MovieIds[1];

    private Guid MovieThree => fixture.MovieIds[2];

    [Fact]
    public async Task Register_signs_in_and_logout_starts_a_new_anonymous_session()
    {
        using var browser = fixture.CreateClient();
        (await MeAsync(browser)).ShouldSatisfyAllConditions(
            me => me.Authenticated.ShouldBeFalse(),
            me => me.Email.ShouldBeNull(),
            me => me.Roles.ShouldBeEmpty());

        var email = await RegisterAsync(browser);

        var me = await MeAsync(browser);
        me.Authenticated.ShouldBeTrue();
        me.Email.ShouldBe(email);
        me.Roles.ShouldBeEmpty();

        await AddToCartAsync(browser, MovieOne, OfferType.Buy);
        await LogoutAsync(browser);

        (await MeAsync(browser)).Authenticated.ShouldBeFalse();
        (await browser.GetFromJsonAsync<CartView>("/api/cart", Json, Ct))!.Items.ShouldBeEmpty();

        // The cart belongs to the account, not to the browser.
        await LoginAsync(browser, email);
        (await browser.GetFromJsonAsync<CartView>("/api/cart", Json, Ct))!.Items.ShouldHaveSingleItem().MovieId.ShouldBe(MovieOne);
    }

    [Fact]
    public async Task Register_refuses_a_taken_email_a_short_password_and_an_invalid_email()
    {
        using var first = fixture.CreateClient();
        var email = await RegisterAsync(first);

        using var browser = fixture.CreateClient();
        using var taken = await browser.PostAsJsonAsync("/api/auth/register", new { email = email.ToUpperInvariant(), password = "another password" }, Ct);
        using var shortPassword = await browser.PostAsJsonAsync("/api/auth/register", new { email = "short@filmsearch.test", password = "1234567" }, Ct);
        using var invalidEmail = await browser.PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", password = "long enough password" }, Ct);

        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblemAsync(taken)).Extensions["code"]!.ToString().ShouldBe(AuthErrorCodes.EmailTaken);
        shortPassword.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await shortPassword.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Ct))!.Errors.Keys.ShouldContain(k => k.Equals("password", StringComparison.OrdinalIgnoreCase));
        invalidEmail.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MeAsync(browser)).Authenticated.ShouldBeFalse();
    }

    [Fact]
    public async Task Login_does_not_tell_an_unknown_email_from_a_wrong_password_and_locks_out_after_five_failures()
    {
        using var owner = fixture.CreateClient();
        var email = await RegisterAsync(owner);

        using var browser = fixture.CreateClient();
        using var unknown = await browser.PostAsJsonAsync("/api/auth/login", new { email = "nobody@filmsearch.test", password = "whatever password" }, Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblemAsync(unknown)).Extensions["code"]!.ToString().ShouldBe(AuthErrorCodes.InvalidCredentials);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var wrong = await browser.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong password" }, Ct);
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await ReadProblemAsync(wrong)).Extensions["code"]!.ToString().ShouldBe(AuthErrorCodes.InvalidCredentials);
        }

        using var fifth = await browser.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong password" }, Ct);
        (await ReadProblemAsync(fifth)).Extensions["code"]!.ToString().ShouldBe(AuthErrorCodes.LockedOut);

        // Locked out even with the right password.
        using var right = await browser.PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse battery" }, Ct);
        right.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblemAsync(right)).Extensions["code"]!.ToString().ShouldBe(AuthErrorCodes.LockedOut);
        (await MeAsync(browser)).Authenticated.ShouldBeFalse();
    }

    [Fact]
    public async Task Anonymous_cart_orders_library_and_conversations_survive_login()
    {
        using var setup = fixture.CreateClient();
        var email = await RegisterAsync(setup);

        // An anonymous visitor buys a movie, fills the cart and talks to the assistant.
        using var browser = fixture.CreateClient();
        await AddToCartAsync(browser, MovieTwo, OfferType.Buy);
        var order = await CreateOrderAsync(browser);
        var checkout = await CheckoutAsync(browser, order.Id);
        using (await PressAsync(browser, checkout.CheckoutUrl, "pay"))
        {
            await WaitForStatusAsync(browser, order.Id, OrderStatus.Paid);
        }

        await AddToCartAsync(browser, MovieOne, OfferType.Rent);
        using var created = await browser.PostAsJsonAsync("/api/chat/conversations", new { }, Ct);
        var conversation = (await created.Content.ReadFromJsonAsync<ConversationListItem>(Json, Ct))!;

        using var login = await browser.PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse battery" }, Ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        // The anonymous id is dropped: its data belongs to the account now.
        login.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("fs_sid=;", StringComparison.Ordinal));

        var cart = (await browser.GetFromJsonAsync<CartView>("/api/cart", Json, Ct))!;
        cart.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            i => i.MovieId.ShouldBe(MovieOne),
            i => i.OfferType.ShouldBe(OfferType.Rent));
        (await browser.GetFromJsonAsync<List<OrderView>>("/api/orders", Json, Ct))!.ShouldHaveSingleItem().Id.ShouldBe(order.Id);
        (await GetOrderAsync(browser, order.Id)).Status.ShouldBe(OrderStatus.Paid);
        (await browser.GetFromJsonAsync<List<LibraryItem>>("/api/library", Json, Ct))!.ShouldHaveSingleItem().MovieId.ShouldBe(MovieTwo);
        (await browser.GetFromJsonAsync<List<ConversationListItem>>("/api/chat/conversations", Json, Ct))!.ShouldHaveSingleItem().Id.ShouldBe(conversation.Id);
        using var conversationResponse = await browser.GetAsync($"/api/chat/conversations/{conversation.Id}", Ct);
        conversationResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Signing out leaves everything with the account.
        await LogoutAsync(browser);
        (await browser.GetFromJsonAsync<CartView>("/api/cart", Json, Ct))!.Items.ShouldBeEmpty();
        (await browser.GetFromJsonAsync<List<OrderView>>("/api/orders", Json, Ct))!.ShouldBeEmpty();
        (await browser.GetFromJsonAsync<List<ConversationListItem>>("/api/chat/conversations", Json, Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Merge_keeps_the_newer_line_of_a_movie_and_drops_movies_the_account_owns()
    {
        using var browser = fixture.CreateClient();
        var email = await RegisterAsync(browser);
        await AddToCartAsync(browser, MovieThree, OfferType.Buy);
        var order = await CreateOrderAsync(browser);
        var checkout = await CheckoutAsync(browser, order.Id);
        using (await PressAsync(browser, checkout.CheckoutUrl, "pay"))
        {
            await WaitForStatusAsync(browser, order.Id, OrderStatus.Paid);
        }

        await AddToCartAsync(browser, MovieOne, OfferType.Buy);
        await AddToCartAsync(browser, MovieTwo, OfferType.Buy);
        await LogoutAsync(browser);

        // Anonymous again: rent the movie the account would buy, and add the movie the account already owns.
        await AddToCartAsync(browser, MovieOne, OfferType.Rent);
        await AddToCartAsync(browser, MovieThree, OfferType.Buy);
        await LoginAsync(browser, email);

        var cart = (await browser.GetFromJsonAsync<CartView>("/api/cart", Json, Ct))!;
        cart.Items.Select(i => (i.MovieId, i.OfferType)).ShouldBe([(MovieOne, OfferType.Rent), (MovieTwo, OfferType.Buy)], ignoreOrder: true);
    }

    [Fact]
    public async Task Signing_in_as_another_user_while_signed_in_moves_nothing()
    {
        using var other = fixture.CreateClient();
        var otherEmail = await RegisterAsync(other);
        await LogoutAsync(other);

        using var browser = fixture.CreateClient();
        await RegisterAsync(browser);
        await AddToCartAsync(browser, MovieOne, OfferType.Buy);

        await LoginAsync(browser, otherEmail);

        (await browser.GetFromJsonAsync<CartView>("/api/cart", Json, Ct))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_user_cannot_read_my_order_or_conversation()
    {
        using var mine = fixture.CreateClient();
        await RegisterAsync(mine);
        await AddToCartAsync(mine, MovieOne, OfferType.Buy);
        var order = await CreateOrderAsync(mine);
        using var created = await mine.PostAsJsonAsync("/api/chat/conversations", new { }, Ct);
        var conversation = (await created.Content.ReadFromJsonAsync<ConversationListItem>(Json, Ct))!;

        using var stranger = fixture.CreateClient();
        await RegisterAsync(stranger);

        using var getOrder = await stranger.GetAsync($"/api/orders/{order.Id}", Ct);
        using var payOrder = await stranger.PostAsync($"/api/orders/{order.Id}/checkout", content: null, Ct);
        using var getConversation = await stranger.GetAsync($"/api/chat/conversations/{conversation.Id}", Ct);
        using var postMessage = await stranger.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new { text = "Hello from a stranger" }, Ct);

        getOrder.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        payOrder.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        getConversation.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        postMessage.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.GetFromJsonAsync<List<OrderView>>("/api/orders", Json, Ct))!.ShouldBeEmpty();
        (await stranger.GetFromJsonAsync<List<ConversationListItem>>("/api/chat/conversations", Json, Ct))!.ShouldBeEmpty();

        // Still mine.
        (await GetOrderAsync(mine, order.Id)).Id.ShouldBe(order.Id);
    }

    [Fact]
    public async Task Admin_endpoints_need_the_admin_role()
    {
        using var anonymous = fixture.CreateClient();
        using var user = fixture.CreateClient();
        await RegisterAsync(user);
        using var admin = await fixture.Factory.CreateAdminClientAsync();

        using var anonymousResponse = await anonymous.GetAsync("/api/admin/embeddings", Ct);
        using var userResponse = await user.GetAsync("/api/admin/embeddings", Ct);
        using var adminResponse = await admin.GetAsync("/api/admin/embeddings", Ct);

        anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        userResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        adminResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MeAsync(admin)).Roles.ShouldBe(["admin"]);
    }

    private static async Task<CurrentUser> MeAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CurrentUser>("/api/auth/me", Json, Ct))!;

    private static async Task<ProblemDetails> ReadProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!;
}
