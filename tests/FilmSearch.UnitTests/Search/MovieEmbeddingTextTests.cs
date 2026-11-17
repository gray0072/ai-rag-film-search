using FilmSearch.Core.Catalog;
using FilmSearch.Core.Search;

namespace FilmSearch.UnitTests.Search;

public sealed class MovieEmbeddingTextTests
{
    [Fact]
    public void Builds_the_versioned_template()
    {
        // Snapshot: a change here changes every vector. Bump MovieEmbeddingText.Version together with this text.
        MovieEmbeddingText.Version.ShouldBe("movie-profile-v1");
        MovieEmbeddingText.Build(Inception()).ShouldBe(
            """
            Title: Inception (2010)
            Genres: Action, Science Fiction, Adventure
            Director: Christopher Nolan
            Cast: Leonardo DiCaprio, Joseph Gordon-Levitt, Elliot Page, Tom Hardy, Ken Watanabe
            Keywords: dream, subconscious, heist
            Tagline: Your mind is the scene of the crime.
            Overview: Cobb, a skilled thief who commits corporate espionage by infiltrating the subconscious of his targets, is offered a chance to regain his old life.
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Leaves_out_empty_fields_and_shows_a_different_original_title()
    {
        var movie = Inception() with
        {
            Title = "Унесённые призраками",
            OriginalTitle = "千と千尋の神隠し",
            Year = null,
            Tagline = null,
            Overview = "  ",
            Keywords = [],
            Directors = [],
        };

        MovieEmbeddingText.Build(movie).ShouldBe(
            """
            Title: Унесённые призраками
            Original title: 千と千尋の神隠し
            Genres: Action, Science Fiction, Adventure
            Cast: Leonardo DiCaprio, Joseph Gordon-Levitt, Elliot Page, Tom Hardy, Ken Watanabe
            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Flattens_multi_line_values_and_caps_cast_and_keywords()
    {
        var movie = Inception() with
        {
            Overview = "First paragraph.\r\n\r\nSecond paragraph.",
            Keywords = [.. Enumerable.Range(1, 30).Select(i => $"k{i}")],
        };

        var text = MovieEmbeddingText.Build(movie);

        text.ShouldContain("Overview: First paragraph. Second paragraph.");
        text.ShouldContain("Keywords: k1, k2,");
        text.ShouldContain($"k{MovieEmbeddingText.MaxKeywords}\n");
        text.ShouldNotContain($"k{MovieEmbeddingText.MaxKeywords + 1}");
        text.ShouldNotContain("Cillian Murphy"); // sixth in the cast
    }

    [Fact]
    public void Hash_is_stable_and_changes_with_the_text()
    {
        var text = MovieEmbeddingText.Build(Inception());

        MovieEmbeddingText.Hash(text).ShouldBe(MovieEmbeddingText.Hash(text));
        MovieEmbeddingText.Hash(text).Length.ShouldBe(64);
        MovieEmbeddingText.Hash("title: none | text: " + text).ShouldNotBe(MovieEmbeddingText.Hash(text));
        MovieEmbeddingText.Hash(text + ".").ShouldNotBe(MovieEmbeddingText.Hash(text));
    }

    private static MovieDetails Inception() => new(
        Guid.NewGuid(),
        TmdbId: 27205,
        ImdbId: "tt1375666",
        Title: "Inception",
        OriginalTitle: "Inception",
        OriginalLanguage: "en",
        ReleaseDate: new DateOnly(2010, 7, 15),
        Year: 2010,
        RuntimeMinutes: 148,
        Overview: "Cobb, a skilled thief who commits corporate espionage by infiltrating the subconscious of his targets, is offered a chance to regain his old life.",
        Tagline: "Your mind is the scene of the crime.",
        PosterPath: null,
        BackdropPath: null,
        VoteAverage: 8.4m,
        VoteCount: 38000,
        Keywords: ["dream", "subconscious", "heist"],
        Genres: [new GenreItem(28, "action", "Action"), new GenreItem(878, "science-fiction", "Science Fiction"), new GenreItem(12, "adventure", "Adventure")],
        Directors: [new PersonItem(Guid.NewGuid(), "Christopher Nolan")],
        Writers: [new PersonItem(Guid.NewGuid(), "Christopher Nolan")],
        Cast:
        [
            new CastItem(Guid.NewGuid(), "Leonardo DiCaprio", "Cobb"),
            new CastItem(Guid.NewGuid(), "Joseph Gordon-Levitt", "Arthur"),
            new CastItem(Guid.NewGuid(), "Elliot Page", "Ariadne"),
            new CastItem(Guid.NewGuid(), "Tom Hardy", "Eames"),
            new CastItem(Guid.NewGuid(), "Ken Watanabe", "Saito"),
            new CastItem(Guid.NewGuid(), "Cillian Murphy", "Robert Fischer"),
        ],
        Offers: []);
}
