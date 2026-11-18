using FilmSearch.AI.Providers;
using FilmSearch.Api.Modules.Commerce.FakeGateway;
using FilmSearch.Infrastructure.Commerce.StripeGateway;
using FilmSearch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FilmSearch.IntegrationTests.Infrastructure;

/// <summary>
/// The API in-process (TestServer) against the Testcontainers database. Migrations run on startup; the admin account
/// of <see cref="AuthTestExtensions"/> is seeded; AI health checks are off; the seed file is not loaded on startup (tests that need the catalog use <see cref="CatalogApiFixture"/>).
/// Every embedding profile is served by a <see cref="HashingEmbeddingGenerator"/> of the profile's dimension and every
/// chat profile by <see cref="Chat"/> (a <see cref="ScriptedChatClient"/>), so no test calls a real model. <paramref name="settings"/> are extra configuration values.
/// Fake payment webhooks go through the TestServer handler and are recorded in <see cref="Webhooks"/>; Stripe API calls
/// (when a test configures Stripe) are answered by <see cref="StripeApi"/>; <paramref name="time"/> replaces the clock of the whole app.
/// </summary>
public sealed class FilmSearchApiFactory(
    PostgresFixture postgres,
    string? connectionString = null,
    IReadOnlyDictionary<string, string?>? settings = null,
    TimeProvider? time = null) : WebApplicationFactory<Program>
{
    public ScriptedChatClient Chat { get; } = new();

    public WebhookDeliveryLog Webhooks { get; } = new();

    public FakeStripeApi StripeApi { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{DatabaseSetup.ConnectionName}", connectionString ?? postgres.ConnectionString);
        builder.UseSetting(DatabaseSetup.MigrateOnStartupKey, "true");
        builder.UseSetting("Auth:Admin:Email", AuthTestExtensions.AdminEmail);
        builder.UseSetting("Auth:Admin:Password", AuthTestExtensions.AdminPassword);
        // Tests never call a real model (AGENTS.md): no LM Studio health probe, AI clients are faked.
        builder.UseSetting("AI:HealthChecksEnabled", "false");
        // The fake payment provider calls the TestServer (see below); no delay unless a test asks for one.
        builder.UseSetting("Payments:Fake:WebhookBaseUrl", "http://localhost");
        builder.UseSetting("Payments:Fake:WebhookDelayMinMs", "0");
        builder.UseSetting("Payments:Fake:WebhookDelayMaxMs", "0");
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        // Replaces the provider-backed clients: a later AnyKey registration wins over the one of AddAiProviders.
        builder.ConfigureTestServices(services =>
        {
            services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(KeyedService.AnyKey, (sp, key) =>
                new HashingEmbeddingGenerator(sp.GetRequiredService<IOptions<AiOptions>>().Value.GetEmbeddingProfile((string)key!).Dimensions));
            services.AddKeyedSingleton<IChatClient>(KeyedService.AnyKey, Chat);

            // The lambda runs when the client is created, after the server has started.
            services.AddHttpClient(FakeWebhookDispatcher.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new WebhookDeliveryLog.Handler(Webhooks, Server.CreateHandler()));

            // Never the real Stripe API from tests (AGENTS.md: no network).
            services.AddHttpClient(StripePaymentGateway.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => StripeApi);

            if (time is not null)
            {
                services.AddSingleton(time);
            }
        });
    }
}
