using FilmSearch.AI.QueryUnderstanding;
using FilmSearch.Core.Search;

namespace FilmSearch.UnitTests.AI;

public sealed class QueryUnderstandingValidatorTests
{
    private static readonly HashSet<string> Genres = ["horror", "science-fiction", "comedy"];

    [Fact]
    public void Valid_answer_is_parsed_and_normalized()
    {
        const string json = """
            {"semanticQuery":" space ","filters":{"genresInclude":["Science-Fiction"],"genresExclude":["horror"],
             "yearFrom":1990,"yearTo":"1999","minRating":7,"maxPriceMinor":null,"language":"EN"},
             "similarToTitles":["Alien", " ", "alien"],"detectedLocale":"ru"}
            """;

        QueryUnderstandingValidator.TryParse(json, Genres, out var result, out var error).ShouldBeTrue(error);

        result.ShouldNotBeNull();
        result.SemanticQuery.ShouldBe("space");
        result.Filters.GenresInclude.ShouldBe(["science-fiction"]);
        result.Filters.GenresExclude.ShouldBe(["horror"]);
        result.Filters.YearFrom.ShouldBe(1990);
        result.Filters.YearTo.ShouldBe(1999);
        result.Filters.MinRating.ShouldBe(7);
        result.Filters.Language.ShouldBe("en");
        result.SimilarToTitles.ShouldBe(["Alien"]);
        result.DetectedLocale.ShouldBe("ru");
        result.Source.ShouldBe(QueryUnderstandingSource.Llm);
    }

    [Fact]
    public void Code_fence_around_the_json_is_accepted_and_unknown_locale_is_dropped()
    {
        const string json = """
            ```json
            {"semanticQuery":"","filters":{"genresInclude":[],"genresExclude":[],"yearFrom":null,"yearTo":null,"minRating":null,"maxPriceMinor":null,"language":null},"similarToTitles":[],"detectedLocale":"de"}
            ```
            """;

        QueryUnderstandingValidator.TryParse(json, Genres, out var result, out var error).ShouldBeTrue(error);
        result!.DetectedLocale.ShouldBeNull();
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("null", "empty")]
    [InlineData("""{"semanticQuery":"x","similarToTitles":[]}""", "filters is missing")]
    [InlineData("""{"semanticQuery":"x","filters":{"genresInclude":["thriller"],"genresExclude":[]},"similarToTitles":[]}""", "unknown genre slugs: thriller")]
    [InlineData("""{"semanticQuery":"x","filters":{"genresInclude":["horror"],"genresExclude":["horror"]},"similarToTitles":[]}""", "both in genresInclude and genresExclude")]
    [InlineData("""{"semanticQuery":"x","filters":{"yearFrom":2000,"yearTo":1990},"similarToTitles":[]}""", "yearFrom is greater than yearTo")]
    [InlineData("""{"semanticQuery":"x","filters":{"yearFrom":90},"similarToTitles":[]}""", "yearFrom must be between")]
    [InlineData("""{"semanticQuery":"x","filters":{"minRating":70},"similarToTitles":[]}""", "minRating")]
    [InlineData("""{"semanticQuery":"x","filters":{"maxPriceMinor":-1},"similarToTitles":[]}""", "maxPriceMinor")]
    [InlineData("""{"semanticQuery":"x","filters":{"language":"swedish"},"similarToTitles":[]}""", "ISO 639-1")]
    public void Invalid_answers_are_rejected_with_a_message_for_the_model(string json, string expectedError)
    {
        QueryUnderstandingValidator.TryParse(json, Genres, out var result, out var error).ShouldBeFalse();

        result.ShouldBeNull();
        error.ShouldContain(expectedError, Case.Insensitive);
    }

    [Fact]
    public void Schema_lists_the_genre_slugs_as_an_enum_and_requires_every_property()
    {
        var schema = QueryUnderstandingSchema.Create(["comedy", "horror"]).GetRawText();

        schema.ShouldContain("\"enum\":[\"comedy\",\"horror\"]");
        schema.ShouldContain("\"semanticQuery\"");
        schema.ShouldContain("\"maxPriceMinor\"");
        schema.ShouldContain("\"additionalProperties\":false");
        schema.ShouldContain("\"required\"");
    }
}
