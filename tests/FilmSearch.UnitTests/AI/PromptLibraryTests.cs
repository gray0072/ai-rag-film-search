using FilmSearch.AI.Prompts;

namespace FilmSearch.UnitTests.AI;

public sealed class PromptLibraryTests
{
    [Fact]
    public void Embedded_query_understanding_prompt_is_found_with_its_version()
    {
        var prompt = new PromptLibrary().Get("query-understanding");

        prompt.Version.ShouldBeGreaterThanOrEqualTo(1);
        prompt.Id.ShouldBe($"query-understanding.v{prompt.Version}");
        prompt.Text.ShouldContain("{{genres}}");
        prompt.Text.ShouldNotContain("\r");
        new PromptLibrary().Get("query-understanding", 1).Id.ShouldBe("query-understanding.v1");
    }

    [Fact]
    public void Unknown_prompt_or_version_fails_clearly()
    {
        var library = new PromptLibrary();

        Should.Throw<InvalidOperationException>(() => library.Get("no-such-prompt")).Message.ShouldContain("no-such-prompt");
        Should.Throw<InvalidOperationException>(() => library.Get("query-understanding", 999)).Message.ShouldContain("999");
    }

    [Fact]
    public void Render_replaces_placeholders_and_rejects_missing_or_unused_values()
    {
        var template = new PromptTemplate("test", 2, "Genres: {{genres}}. Year {{current_year}}.");

        template.Render(new Dictionary<string, string> { ["genres"] = "drama", ["current_year"] = "2026" })
            .ShouldBe("Genres: drama. Year 2026.");
        Should.Throw<InvalidOperationException>(() => template.Render(new Dictionary<string, string> { ["genres"] = "drama" }))
            .Message.ShouldContain("current_year");
        Should.Throw<InvalidOperationException>(() => template.Render(new Dictionary<string, string>
        {
            ["genres"] = "drama",
            ["current_year"] = "2026",
            ["extra"] = "x",
        })).Message.ShouldContain("extra");
    }
}
