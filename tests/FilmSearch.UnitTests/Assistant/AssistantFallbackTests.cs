using FilmSearch.AI.Providers;
using FilmSearch.AI.Telemetry;
using FilmSearch.Core.Assistant;
using FilmSearch.UnitTests.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace FilmSearch.UnitTests.Assistant;

/// <summary>An answer written by the fallback chat profile (I19): flagged on the event and the stored message, priced with the fallback's model.</summary>
public sealed class AssistantFallbackTests
{
    [Fact]
    public async Task Answer_of_the_fallback_profile_is_flagged_and_priced_with_its_model()
    {
        var harness = new AssistantHarness();
        harness.Ai.ChatProfiles["backup"] = new ChatProfileOptions { Endpoint = "https://backup.test/v1", Model = "backup-model", ApiKey = "key" };
        harness.Ai.Pricing["backup-model"] = new ModelPricing { InputPerMTokUsd = 2, OutputPerMTokUsd = 20 };
        harness.Ai.FallbackChatProfile = "backup";
        var localDown = new ChaosChatClient("unused") { Failure = new HttpRequestException("Connection refused (localhost:1234)") };
        harness.Client = new FallbackChatClient(localDown, "local", harness.Chat, "backup", new AiMetrics(harness.MeterFactory), NullLogger<FallbackChatClient>.Instance);
        harness.Chat.Answer(ScriptedStreamingChatClient.Text("Here is a movie for tonight."));
        var conversation = harness.Store.Add("en", "local");

        var events = await harness.SendAsync(conversation, "Something for tonight?");

        events.OfType<ErrorEvent>().ShouldBeEmpty();
        events.OfType<MessageCompletedEvent>().ShouldHaveSingleItem().Fallback.ShouldBeTrue();
        var answer = harness.Store.Messages.Single(m => m.Role == MessageRole.Assistant);
        answer.Fallback.ShouldBeTrue();
        answer.Content.ShouldBe("Here is a movie for tonight.");
        // The scripted call uses 100 input and 10 output tokens: 100 × $2/M + 10 × $20/M.
        answer.EstimatedCostUsd.ShouldBe(0.0004m);
        conversation.ProfileName.ShouldBe("backup");
    }

    [Fact]
    public async Task Answer_of_the_active_profile_is_not_flagged()
    {
        var harness = new AssistantHarness();
        harness.Chat.Answer(ScriptedStreamingChatClient.Text("Here you go."));
        var conversation = harness.Store.Add("en", "local");

        var events = await harness.SendAsync(conversation, "Hi there");

        events.OfType<MessageCompletedEvent>().ShouldHaveSingleItem().Fallback.ShouldBeFalse();
        harness.Store.Messages.Single(m => m.Role == MessageRole.Assistant).Fallback.ShouldBeFalse();
    }
}
