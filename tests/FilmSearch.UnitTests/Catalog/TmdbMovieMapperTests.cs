using System.Text.Json;
using FilmSearch.Core.Catalog;
using FilmSearch.Infrastructure.Tmdb;

namespace FilmSearch.UnitTests.Catalog;

public sealed class TmdbMovieMapperTests
{
    // Trimmed TMDB /movie/{id}?append_to_response=credits,keywords,translations,external_ids response.
    private const string DetailsJson = """
        {
          "id": 278,
          "imdb_id": "tt0111161",
          "title": "The Shawshank Redemption",
          "original_title": "The Shawshank Redemption",
          "original_language": "en",
          "overview": "Imprisoned in the 1940s for the double murder of his wife and her lover...",
          "tagline": "Fear can hold you prisoner. Hope can set you free.",
          "release_date": "1994-09-23",
          "runtime": 142,
          "poster_path": "/poster.jpg",
          "backdrop_path": "",
          "vote_average": 8.712,
          "vote_count": 28000,
          "popularity": 123.45678,
          "adult": false,
          "genres": [{ "id": 18, "name": "Drama" }, { "id": 80, "name": "Crime" }],
          "credits": {
            "cast": [
              { "id": 2, "name": "Morgan Freeman", "character": "Red", "order": 1 },
              { "id": 1, "name": "Tim Robbins", "character": "Andy Dufresne", "order": 0 }
            ],
            "crew": [
              { "id": 10, "name": "Frank Darabont", "job": "Director", "department": "Directing" },
              { "id": 10, "name": "Frank Darabont", "job": "Screenplay", "department": "Writing" },
              { "id": 11, "name": "Stephen King", "job": "Novel", "department": "Writing" },
              { "id": 12, "name": "Thomas Newman", "job": "Original Music Composer", "department": "Sound" }
            ]
          },
          "keywords": { "keywords": [{ "id": 1, "name": "prison" }, { "id": 2, "name": "prison" }, { "id": 3, "name": "hope" }] },
          "translations": {
            "translations": [
              { "iso_3166_1": "UA", "iso_639_1": "ru", "data": { "title": "Другой", "overview": "", "tagline": "" } },
              { "iso_3166_1": "RU", "iso_639_1": "ru", "data": { "title": "Побег из Шоушенка", "overview": "Описание", "tagline": "" } },
              { "iso_3166_1": "SE", "iso_639_1": "sv", "data": { "title": "Nyckeln till frihet", "overview": "", "tagline": "" } },
              { "iso_3166_1": "DE", "iso_639_1": "de", "data": { "title": "Die Verurteilten", "overview": "", "tagline": "" } }
            ]
          },
          "external_ids": { "imdb_id": "tt0111161" }
        }
        """;

    private static MovieImport Map() =>
        TmdbMovieMapper.ToImport(JsonSerializer.Deserialize<TmdbMovieDetails>(DetailsJson, TmdbJson.Options)!);

    [Fact]
    public void Maps_movie_fields()
    {
        var movie = Map();

        movie.TmdbId.ShouldBe(278);
        movie.ImdbId.ShouldBe("tt0111161");
        movie.OriginalLanguage.ShouldBe("en");
        movie.ReleaseDate.ShouldBe(new DateOnly(1994, 9, 23));
        movie.RuntimeMinutes.ShouldBe(142);
        movie.PosterPath.ShouldBe("/poster.jpg");
        movie.BackdropPath.ShouldBeNull();
        movie.VoteAverage.ShouldBe(8.7m);
        movie.GenreIds.ShouldBe([18, 80]);
        movie.Keywords.ShouldBe(["prison", "hope"]);
    }

    [Fact]
    public void Picks_translations_of_supported_locales_preferring_the_main_region()
    {
        var movie = Map();

        movie.Translations.Keys.ShouldBe(["en", "ru", "sv"], ignoreOrder: true);
        movie.Translations["en"].Tagline.ShouldBe("Fear can hold you prisoner. Hope can set you free.");
        movie.Translations["ru"].ShouldBe(new TranslationImport("Побег из Шоушенка", "Описание", null));
        movie.Translations["sv"].ShouldBe(new TranslationImport("Nyckeln till frihet", null, null));
    }

    [Fact]
    public void Maps_directors_writers_and_cast_in_billing_order()
    {
        var credits = Map().Credits;

        credits.Where(c => c.Role == CreditRole.Director).Select(c => c.Name).ShouldBe(["Frank Darabont"]);
        credits.Where(c => c.Role == CreditRole.Writer).Select(c => c.Name).ShouldBe(["Frank Darabont", "Stephen King"]);
        credits.Where(c => c.Role == CreditRole.Cast).Select(c => (c.Name, c.Order)).ShouldBe([("Tim Robbins", 0), ("Morgan Freeman", 1)]);
        credits.ShouldAllBe(c => c.PersonTmdbId != null);
    }

    [Fact]
    public void Empty_release_date_maps_to_null()
    {
        var details = JsonSerializer.Deserialize<TmdbMovieDetails>(DetailsJson, TmdbJson.Options)! with { ReleaseDate = "" };

        TmdbMovieMapper.ToImport(details).ReleaseDate.ShouldBeNull();
    }

    [Theory]
    [InlineData("Science Fiction", "science-fiction")]
    [InlineData("TV Movie", "tv-movie")]
    [InlineData("Drama", "drama")]
    public void Genre_slugs_are_built_from_english_names(string name, string slug)
    {
        TmdbMovieMapper.Slugify(name).ShouldBe(slug);
    }
}
