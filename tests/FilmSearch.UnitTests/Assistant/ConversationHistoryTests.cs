using FilmSearch.AI.Assistant;
using FilmSearch.AI.Prompts;
using FilmSearch.Core.Assistant;
using Microsoft.Extensions.AI;

namespace FilmSearch.UnitTests.Assistant;

public sealed class ConversationHistoryTests
{
    private int _seq;

    [Fact]
    public void Split_keeps_whole_recent_turns_within_the_message_limit()
    {
        List<ConversationMessage> messages =
        [
            User("one"), Answer("a1"),
            User("two"), ToolRound(), Tool("""{"movies":[]}"""), Answer("a2"),
            User("three"), Answer("a3"),
        ];

        var (older, window) = ConversationHistory.Split(messages, maxVisibleMessages: 4, tokenBudget: 10_000);

        older.Select(m => m.Content).ShouldBe(["one", "a1"]);
        window[0].Content.ShouldBe("two");
        window.Count.ShouldBe(6);
    }

    [Fact]
    public void Split_respects_the_token_budget_but_always_keeps_the_last_turn()
    {
        List<ConversationMessage> messages = [User("short"), Answer("short"), User(new string('x', 4000)), Answer("long")];

        var (older, window) = ConversationHistory.Split(messages, maxVisibleMessages: 100, tokenBudget: 500);

        older.Count.ShouldBe(2);
        window.Count.ShouldBe(2);
    }

    [Fact]
    public void Earlier_tool_results_keep_only_id_title_and_year()
    {
        var compact = ConversationHistory.CompactToolResult(
            """{"movies":[{"id":"0199a1b2-0000-7000-8000-000000000001","title":"Moon","year":2009,"overview":"long text","offers":["buy: 9.99 USD"]}]}""");

        compact.ShouldBe("""{"movies":[{"id":"0199a1b2-0000-7000-8000-000000000001","title":"Moon","year":2009}]}""");
        ConversationHistory.CompactToolResult("""{"error":"bad"}""").ShouldBe("""{"error":"bad"}""");
        ConversationHistory.CompactToolResult(new string('y', 2000)).Length.ShouldBe(1001);
    }

    [Fact]
    public void Tool_rounds_are_sent_as_calls_without_their_text()
    {
        var round = ToolRound();
        round.Content = "Let me search.";

        var chat = ConversationHistory.ToChatMessages([User("space"), round, Tool("""{"movies":[]}"""), Answer("Nothing found.")]);

        chat.Select(m => m.Role).ShouldBe([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant]);
        chat[1].Text.ShouldBeEmpty();
        chat[1].Contents.OfType<FunctionCallContent>().Single().CallId.ShouldBe("c1");
        chat[2].Contents.OfType<FunctionResultContent>().Single().CallId.ShouldBe("c1");
    }

    [Fact]
    public void Summary_transcript_lists_messages_and_found_movies_with_ids()
    {
        var transcript = ConversationSummarizer.Transcript(
            "Earlier: likes dramas.",
            [User("space?"), ToolRound(), Tool("""{"movies":[{"id":"0199a1b2-0000-7000-8000-000000000001","title":"Moon","year":2009}]}"""), Answer("Watch Moon.")]);

        transcript.ShouldBe(
            "Earlier summary:\nEarlier: likes dramas.\n\nConversation:\nUser: space?\n(Tool found: Moon (2009) [0199a1b2-0000-7000-8000-000000000001])\nAssistant: Watch Moon.\n");
    }

    [Fact]
    public void Assistant_prompts_render_without_placeholders_left()
    {
        var library = new PromptLibrary();

        var system = library.Get(AssistantChat.PromptName).Render(new Dictionary<string, string> { ["current_year"] = "2026" });
        var summary = library.Get(ConversationSummarizer.PromptName).Render(new Dictionary<string, string>());

        system.ShouldContain("search_movies");
        system.ShouldContain("2026");
        system.ShouldNotContain("{{");
        summary.ShouldNotContain("{{");
    }

    [Fact]
    public void Title_is_the_first_message_on_one_line_and_cut()
    {
        AssistantChat.Title("  space\n movies ").ShouldBe("space movies");
        AssistantChat.Title(new string('a', 100)).Length.ShouldBe(AssistantLimits.MaxTitleLength + 1);
    }

    private ConversationMessage User(string text) => new() { Seq = ++_seq, Role = MessageRole.User, Content = text };

    private ConversationMessage Answer(string text) => new() { Seq = ++_seq, Role = MessageRole.Assistant, Content = text };

    private ConversationMessage ToolRound() => new()
    {
        Seq = ++_seq,
        Role = MessageRole.Assistant,
        ToolCallsJson = ConversationHistory.SerializeToolCalls([new FunctionCallContent("c1", "search_movies", new Dictionary<string, object?> { ["query"] = "space" })]),
    };

    private ConversationMessage Tool(string json) => new() { Seq = ++_seq, Role = MessageRole.Tool, ToolCallId = "c1", Content = json };
}
