using FilmSearch.AI.Assistant;
using FilmSearch.AI.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FilmSearch.UnitTests.AI;

/// <summary>
/// I20: the configuration of the demo environment (appsettings.json + appsettings.Demo.json, copied to the output by the
/// API project, plus the variables deploy/compose.yaml sets) passes the startup validation, so a typo fails here and
/// not on the server.
/// </summary>
public sealed class DemoConfigurationTests
{
    private static IConfiguration Demo(params (string Key, string? Value)[] environment) =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Demo.json", optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:ChatProfiles:openrouter:ApiKey"] = "test-key",
                ["AI:EmbeddingProfiles:openrouter:ApiKey"] = "test-key",
            })
            .AddInMemoryCollection(environment.Select(e => KeyValuePair.Create(e.Key, e.Value)))
            .Build();

    private static AiOptions Ai(IConfiguration configuration) =>
        configuration.GetSection(AiOptions.SectionName).Get<AiOptions>()!;

    [Fact]
    public void The_demo_uses_OpenRouter_chat_and_embeddings_without_prompt_content_in_telemetry()
    {
        var ai = Ai(Demo());

        new AiOptionsValidator().Validate(null, ai).Succeeded.ShouldBeTrue();
        ai.ActiveChatProfile.ShouldBe("openrouter");
        ai.ActiveEmbeddingProfile.ShouldBe("openrouter");
        ai.GetEmbeddingProfile("openrouter").Dimensions.ShouldBe(1024);
        ai.EnableSensitiveData.ShouldBeFalse();
        ai.LogRawHttp.ShouldBeFalse();
    }

    [Fact]
    public void LM_Studio_on_the_Docker_host_is_valid_without_a_key()
    {
        var ai = Ai(Demo(
            ("AI:ActiveChatProfile", "local"),
            ("AI:ActiveEmbeddingProfile", "local"),
            ("AI:ChatProfiles:openrouter:ApiKey", ""),
            ("AI:EmbeddingProfiles:openrouter:ApiKey", ""),
            ("AI:ChatProfiles:local:Endpoint", "http://host.docker.internal:1234/v1"),
            ("AI:EmbeddingProfiles:local:Endpoint", "http://host.docker.internal:1234/v1")));

        new AiOptionsValidator().Validate(null, ai).Succeeded.ShouldBeTrue();
        ai.GetEmbeddingProfile("local").Dimensions.ShouldBe(768);
    }

    [Fact]
    public void Without_the_OpenRouter_key_the_demo_does_not_start()
    {
        var ai = Ai(Demo(("AI:ChatProfiles:openrouter:ApiKey", "")));

        var result = new AiOptionsValidator().Validate(null, ai);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("AI:ChatProfiles:openrouter:ApiKey");
    }

    [Fact]
    public void The_demo_assistant_budgets_are_valid_and_cap_the_whole_service()
    {
        using var services = new ServiceCollection().AddAssistant(Demo()).BuildServiceProvider();

        var assistant = services.GetRequiredService<IOptions<AssistantOptions>>().Value;

        assistant.MaxTotalTokensPerDay.ShouldBeGreaterThan(0);
        assistant.MaxOwnerTokensPerDay.ShouldBeLessThan(assistant.MaxTotalTokensPerDay);
    }
}
