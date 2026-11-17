using FilmSearch.Core.Localization;

namespace FilmSearch.UnitTests.Catalog;

public sealed class LocalesTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("ru", "ru")]
    [InlineData("sv", "sv")]
    [InlineData("SV", "sv")]
    [InlineData("sv-SE", "sv")]
    [InlineData("ru_RU", "ru")]
    [InlineData(" ru ", "ru")]
    [InlineData("fr", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void Normalize_maps_language_tags_to_supported_locales(string? tag, string expected)
    {
        Locales.Normalize(tag).ShouldBe(expected);
    }
}
