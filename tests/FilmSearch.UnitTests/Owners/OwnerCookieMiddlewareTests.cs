using FilmSearch.Api.Owners;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace FilmSearch.UnitTests.Owners;

public sealed class OwnerCookieMiddlewareTests
{
    private static readonly OwnerCookieMiddleware Middleware = new(_ => Task.CompletedTask);

    [Fact]
    public async Task Api_request_without_cookie_gets_new_owner_and_cookie()
    {
        var context = CreateContext("/api/cart");
        var owner = new OwnerContext();

        await Middleware.InvokeAsync(context, owner);

        owner.OwnerId.ShouldNotBe(Guid.Empty);
        var setCookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.ToString());
        setCookie.Name.ToString().ShouldBe(OwnerCookie.Name);
        setCookie.Value.ToString().ShouldBe(OwnerCookie.Format(owner.OwnerId));
        setCookie.HttpOnly.ShouldBeTrue();
        setCookie.Secure.ShouldBeTrue();
        setCookie.SameSite.ShouldBe(Microsoft.Net.Http.Headers.SameSiteMode.Lax);
    }

    [Fact]
    public async Task Api_request_with_valid_cookie_keeps_owner_and_sets_no_cookie()
    {
        var existing = OwnerCookie.NewId();
        var context = CreateContext("/api/cart", OwnerCookie.Format(existing));
        var owner = new OwnerContext();

        await Middleware.InvokeAsync(context, owner);

        owner.OwnerId.ShouldBe(existing);
        context.Response.Headers.SetCookie.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000000000000000000000000000")]
    public async Task Api_request_with_bad_cookie_gets_new_owner(string cookie)
    {
        var context = CreateContext("/api/cart", cookie);
        var owner = new OwnerContext();

        await Middleware.InvokeAsync(context, owner);

        owner.OwnerId.ShouldNotBe(Guid.Empty);
        context.Response.Headers.SetCookie.ToString().ShouldContain(OwnerCookie.Format(owner.OwnerId));
    }

    [Fact]
    public async Task Non_api_request_does_not_resolve_owner()
    {
        var context = CreateContext("/health");
        var owner = new OwnerContext();

        await Middleware.InvokeAsync(context, owner);

        owner.IsResolved.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => owner.OwnerId);
        context.Response.Headers.SetCookie.ShouldBeEmpty();
    }

    [Fact]
    public void New_ids_are_unique()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => OwnerCookie.NewId()).ToHashSet();

        ids.Count.ShouldBe(1000);
    }

    private static DefaultHttpContext CreateContext(string path, string? cookie = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (cookie is not null)
        {
            context.Request.Headers.Cookie = $"{OwnerCookie.Name}={cookie}";
        }

        return context;
    }
}
