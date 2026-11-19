using System.Net;
using System.Net.Http.Json;
using FilmSearch.Api.Modules.Admin;
using FilmSearch.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.IntegrationTests;

public sealed class AdminAiApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly FilmSearchApiFactory _factory = new(postgres);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Chat_goes_to_the_requested_profile_and_returns_usage()
    {
        var fake = new FakeChatClient();
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AI:ChatProfiles:fake:Endpoint", "http://fake.test/v1");
            builder.UseSetting("AI:ChatProfiles:fake:Model", "fake-model");
            builder.UseSetting("AI:ChatProfiles:fake:ApiKey", "unused");
            builder.ConfigureTestServices(services => services.AddKeyedSingleton<IChatClient>("fake", fake));
        });
        using var client = await factory.CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/admin/ai/chat", new { message = "Hi", profile = "fake" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AdminAiChatResponse>(Ct);
        body.ShouldNotBeNull();
        body.Profile.ShouldBe("fake");
        body.Model.ShouldBe("fake-model");
        body.Text.ShouldBe("echo: Hi");
        fake.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Profiles_list_the_configured_profiles_without_keys()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AI:ChatProfiles:keyless:Endpoint", "http://keyless.test/v1");
            builder.UseSetting("AI:ChatProfiles:keyless:Model", "keyless-model");
            builder.UseSetting("AI:ChatProfiles:keyless:ApiKey", "");
            builder.UseSetting("AI:ChatProfiles:keyless:SupportsTools", "false");
        });
        using var client = await factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/api/admin/ai/profiles", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync(Ct);
        json.ShouldNotContain("keyless.test");
        var body = await response.Content.ReadFromJsonAsync<AdminAiProfiles>(Ct);
        body.ShouldNotBeNull();
        body.ChatProfiles.ShouldContain(p => p.Name == body.ActiveChatProfile);
        body.EmbeddingProfiles.ShouldContain(p => p.Name == body.ActiveEmbeddingProfile && p.Dimensions > 0);
        body.ChatProfiles.ShouldContain(new AdminChatProfile("keyless", "keyless-model", HasApiKey: false, SupportsTools: false));
    }

    [Fact]
    public async Task Profiles_need_the_admin_role()
    {
        using var client = _factory.CreateBrowser();
        await AuthTestExtensions.RegisterAsync(client);

        var response = await client.GetAsync("/api/admin/ai/profiles", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_profile_returns_validation_problem()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/admin/ai/chat", new { message = "Hi", profile = "nope" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Unknown chat profile 'nope'");
    }

    [Fact]
    public async Task Empty_message_returns_validation_problem()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/admin/ai/chat", new { message = "" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private sealed class FakeChatClient : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "echo: " + messages.Last().Text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
