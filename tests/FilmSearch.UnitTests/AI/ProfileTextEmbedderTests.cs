using System.Net;
using FilmSearch.Core.Search;
using Microsoft.Extensions.DependencyInjection;

namespace FilmSearch.UnitTests.AI;

/// <summary><see cref="ITextEmbedder"/> over the real AI registration and a fake OpenAI-compatible endpoint.</summary>
public sealed class ProfileTextEmbedderTests
{
    private static readonly Dictionary<string, string?> Prefixes = new()
    {
        ["AI:EmbeddingProfiles:local:QueryPrefix"] = "query: ",
        ["AI:EmbeddingProfiles:local:DocumentPrefix"] = "passage: ",
        ["AI:EmbeddingProfiles:Second:Endpoint"] = "http://second.test/v1",
        ["AI:EmbeddingProfiles:Second:Model"] = "second-model",
        ["AI:EmbeddingProfiles:Second:ApiKey"] = "key",
        ["AI:EmbeddingProfiles:Second:Dimensions"] = "4",
        ["AI:EmbeddingProfiles:Second:MaxRetries"] = "0",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Active_model_comes_from_the_active_profile()
    {
        using var host = AiTestHost.Build(new FakeProviderHandler(), Prefixes);
        var embedder = host.Services.GetRequiredService<ITextEmbedder>();

        embedder.Active.ShouldBe(new EmbeddingModel("local", "embedding-model", 3, "query: ", "passage: "));
        embedder.Active.FormatQuery("heist in dreams").ShouldBe("query: heist in dreams");
        embedder.Active.FormatDocument("Title: Inception").ShouldBe("passage: Title: Inception");
    }

    [Fact]
    public void Profiles_are_found_case_insensitively_under_their_configured_name()
    {
        using var host = AiTestHost.Build(new FakeProviderHandler(), Prefixes);
        var embedder = host.Services.GetRequiredService<ITextEmbedder>();

        embedder.GetModel("second").Profile.ShouldBe("Second");
        Should.Throw<ArgumentException>(() => embedder.GetModel("nope")).Message.ShouldContain("Unknown embedding profile 'nope'");
    }

    [Fact]
    public async Task Embeds_the_given_texts_with_the_profile_model()
    {
        var handler = new FakeProviderHandler();
        using var host = AiTestHost.Build(handler, Prefixes);
        var embedder = host.Services.GetRequiredService<ITextEmbedder>();
        var model = embedder.Active;

        var vectors = await embedder.EmbedAsync(model, [model.FormatQuery("heist in dreams")], Ct);

        vectors.Count.ShouldBe(1);
        vectors[0].ToArray().ShouldBe([0.1f, 0.2f, 0.3f]);
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldEndWith("/embeddings");
        request.Body.ShouldContain("\"model\":\"embedding-model\"");
        request.Body.ShouldContain("query: heist in dreams");
    }

    [Fact]
    public async Task Vectors_of_another_length_than_the_profile_says_are_rejected()
    {
        using var host = AiTestHost.Build(new FakeProviderHandler(), Prefixes);
        var embedder = host.Services.GetRequiredService<ITextEmbedder>();

        // The fake endpoint returns 3 numbers; profile "Second" says 4.
        var error = await Should.ThrowAsync<InvalidOperationException>(() => embedder.EmbedAsync(embedder.GetModel("Second"), ["x"], Ct));

        error.Message.ShouldContain("3 dimensions");
        error.Message.ShouldContain("AI:EmbeddingProfiles:Second:Dimensions");
    }

    [Fact]
    public async Task Provider_failures_become_AiProviderUnavailableException()
    {
        var handler = new FakeProviderHandler
        {
            Respond = _ => FakeProviderHandler.Json("""{"error":{"message":"model not loaded"}}""", HttpStatusCode.InternalServerError),
        };
        using var host = AiTestHost.Build(handler, Prefixes);
        var embedder = host.Services.GetRequiredService<ITextEmbedder>();

        var error = await Should.ThrowAsync<AiProviderUnavailableException>(() => embedder.EmbedAsync(embedder.Active, ["x"], Ct));

        error.Message.ShouldContain("Embedding profile 'local'");
    }

    [Fact]
    public async Task Unreachable_provider_becomes_AiProviderUnavailableException()
    {
        // LM Studio server stopped: the connection is refused.
        // With retries on, the OpenAI client throws an AggregateException after the last failed attempt.
        var handler = new FakeProviderHandler { Respond = _ => throw new HttpRequestException("Connection refused (local.test:80)") };
        using var host = AiTestHost.Build(handler, new Dictionary<string, string?>(Prefixes) { ["AI:EmbeddingProfiles:local:MaxRetries"] = "1" });
        var embedder = host.Services.GetRequiredService<ITextEmbedder>();

        var error = await Should.ThrowAsync<AiProviderUnavailableException>(() => embedder.EmbedAsync(embedder.Active, ["x"], Ct));

        error.Message.ShouldContain("Connection refused");
        error.InnerException.ShouldBeOfType<AggregateException>();
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task No_texts_means_no_call()
    {
        var handler = new FakeProviderHandler();
        using var host = AiTestHost.Build(handler, Prefixes);
        var embedder = host.Services.GetRequiredService<ITextEmbedder>();

        (await embedder.EmbedAsync(embedder.Active, [], Ct)).ShouldBeEmpty();
        handler.Requests.ShouldBeEmpty();
    }
}
