using FilmSearch.AI.Providers;

namespace FilmSearch.UnitTests.AI;

public sealed class AiOptionsValidatorTests
{
    private static readonly AiOptionsValidator Validator = new();

    private static AiOptions Valid() => new()
    {
        ActiveChatProfile = "local",
        ActiveEmbeddingProfile = "local",
        ChatProfiles =
        {
            ["local"] = new() { Endpoint = "http://localhost:1234/v1", Model = "google/gemma-4-e4b", ApiKey = "lm-studio", Temperature = 0.3f },
            ["openrouter"] = new() { Endpoint = "https://openrouter.ai/api/v1", Model = "openai/gpt-oss-20b" },
        },
        EmbeddingProfiles =
        {
            ["local"] = new() { Endpoint = "http://localhost:1234/v1", Model = "text-embedding-embeddinggemma-300m", ApiKey = "lm-studio", Dimensions = 768 },
        },
    };

    private static IEnumerable<string> Failures(AiOptions options) => Validator.Validate(null, options).Failures ?? [];

    [Fact]
    public void Valid_options_pass_even_when_an_inactive_profile_has_no_key()
    {
        Validator.Validate(null, Valid()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Unknown_active_profiles_fail()
    {
        var options = Valid();
        options.ActiveChatProfile = "cloud";
        options.ActiveEmbeddingProfile = "cloud";

        var failures = Failures(options);

        failures.ShouldContain(f => f.Contains("AI:ActiveChatProfile 'cloud'", StringComparison.Ordinal));
        failures.ShouldContain(f => f.Contains("AI:ActiveEmbeddingProfile 'cloud'", StringComparison.Ordinal));
    }

    [Fact]
    public void Profile_names_are_case_insensitive()
    {
        var options = Valid();
        options.ActiveChatProfile = "LOCAL";

        Validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Active_profile_needs_an_api_key()
    {
        var options = Valid();
        options.ActiveChatProfile = "openrouter";

        Failures(options).ShouldHaveSingleItem().ShouldContain("dotnet user-secrets set AI:ChatProfiles:openrouter:ApiKey");
    }

    [Theory]
    [InlineData("localhost:1234/v1")]
    [InlineData("ftp://example.com")]
    [InlineData("")]
    public void Endpoint_must_be_an_absolute_http_url(string endpoint)
    {
        var options = Valid();
        options.ChatProfiles["local"].Endpoint = endpoint;

        Failures(options).ShouldHaveSingleItem().ShouldContain("Endpoint");
    }

    [Fact]
    public void Out_of_range_values_fail()
    {
        var options = Valid();
        options.ChatProfiles["local"].Temperature = 3;
        options.ChatProfiles["local"].MaxOutputTokens = 0;
        options.ChatProfiles["local"].Model = " ";
        options.EmbeddingProfiles["local"].Dimensions = 0;
        options.Pricing["x"] = new ModelPricing { InputPerMTokUsd = -1 };

        var failures = Failures(options);

        failures.Count().ShouldBe(5);
    }

    [Fact]
    public void Pricing_estimates_cost_per_million_tokens()
    {
        var pricing = new ModelPricing { InputPerMTokUsd = 0.018m, OutputPerMTokUsd = 0.09m };

        pricing.Estimate(1_000_000, 0).ShouldBe(0.018m);
        pricing.Estimate(2_000, 500).ShouldBe(0.000081m);
    }

    [Fact]
    public void Embedding_profile_adds_query_and_document_prefixes()
    {
        var profile = new EmbeddingProfileOptions { QueryPrefix = "task: search result | query: ", DocumentPrefix = "title: none | text: " };

        profile.FormatQuery("heist in dreams").ShouldBe("task: search result | query: heist in dreams");
        profile.FormatDocument("A thief…").ShouldBe("title: none | text: A thief…");
    }
}
