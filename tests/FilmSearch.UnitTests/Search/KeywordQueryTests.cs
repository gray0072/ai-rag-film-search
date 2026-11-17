using FilmSearch.Infrastructure.Search.Keyword;

namespace FilmSearch.UnitTests.Search;

public sealed class KeywordQueryTests
{
    [Theory]
    [InlineData("  Крёстный отец ", "Крестный отец")]
    [InlineData("ЁЖИК", "ЕЖИК")]
    [InlineData("nolan dream", "nolan dream")]
    public void Normalize_folds_yo_and_trims(string text, string expected) =>
        KeywordQuery.Normalize(text).ShouldBe(expected);

    [Theory]
    [InlineData("nolan dream", "nolan or dream")]
    [InlineData("\"dark knight\" -rises", "dark or knight or rises")]
    [InlineData("war or peace", "war or peace")]
    [InlineData("Крестный  отец, 1972!", "Крестный or отец or 1972")]
    [InlineData("Love love LOVE story", "Love or story")]
    public void AnyWord_joins_words_with_or(string text, string expected) =>
        KeywordQuery.AnyWord(text).ShouldBe(expected);

    [Theory]
    [InlineData("godfather")]
    [InlineData("  !!  ")]
    [InlineData("or or matrix")]
    public void AnyWord_is_null_for_fewer_than_two_words(string text) =>
        KeywordQuery.AnyWord(text).ShouldBeNull();
}
