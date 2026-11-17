using System.Text.Json;
using FilmSearch.Core.Ingestion;
using FilmSearch.Core.Localization;

namespace FilmSearch.UnitTests.Catalog;

/// <summary>Checks the committed seed file (copied to the output by the API project) against the format in data/seed/README.md.</summary>
public sealed class SeedFileTests
{
    private static readonly Lazy<SeedFile> Seed = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Seed", "movies.seed.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<SeedFile>(stream, SeedJson.Options)!;
    });

    [Fact]
    public void Seed_file_has_current_version_and_movies()
    {
        Seed.Value.Version.ShouldBe(SeedFile.CurrentVersion);
        Seed.Value.Source.ShouldBeOneOf("tmdb", "hand-made");
        Seed.Value.Movies.Count.ShouldBeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public void Movies_are_unique_and_sorted_by_tmdb_id()
    {
        var ids = Seed.Value.Movies.Select(m => m.TmdbId).ToList();

        ids.ShouldBeUnique();
        ids.ShouldBe(ids.Order().ToList());
    }

    [Fact]
    public void Every_movie_has_an_english_title()
    {
        Seed.Value.Movies.ShouldAllBe(m =>
            m.Translations.ContainsKey(Locales.English) && !string.IsNullOrWhiteSpace(m.Translations[Locales.English].Title));
    }

    [Fact]
    public void Translations_use_supported_locales_only()
    {
        Seed.Value.Movies.SelectMany(m => m.Translations.Keys).Distinct().ShouldBeSubsetOf(Locales.All);
    }

    [Fact]
    public void Genres_have_names_in_all_locales_and_cover_every_movie()
    {
        var genres = Seed.Value.Genres;
        genres.Select(g => g.Slug).ShouldBeUnique();
        genres.ShouldAllBe(g => Locales.All.All(locale => g.Names.ContainsKey(locale)));

        var genreIds = genres.Select(g => g.Id).ToHashSet();
        Seed.Value.Movies.SelectMany(m => m.GenreIds).Distinct().ShouldAllBe(id => genreIds.Contains(id));
    }

    [Fact]
    public void Ratings_are_on_a_zero_to_ten_scale()
    {
        Seed.Value.Movies.ShouldAllBe(m => m.VoteAverage >= 0 && m.VoteAverage <= 10);
    }
}
