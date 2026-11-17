using FilmSearch.Infrastructure.Search.Semantic;

namespace FilmSearch.UnitTests.Search;

public sealed class QueryVectorsTests
{
    [Fact]
    public void Normalize_scales_to_unit_length_and_keeps_a_zero_vector()
    {
        QueryVectors.Normalize(V(3f, 4f).Span).ShouldBe([0.6f, 0.8f], 1e-6f);
        QueryVectors.Normalize(V(0f, 0f).Span).ShouldBe([0f, 0f]);
    }

    [Fact]
    public void Blend_gives_the_query_and_the_movies_equal_weight()
    {
        // Query (10, 0) → (1, 0); movies (0, 2) and (0, -2) → mean (0, 0): the movies cancel out.
        QueryVectors.Blend(V(10f, 0f), [V(0f, 2f), V(0f, -2f)]).ShouldBe([1f, 0f], 1e-6f);

        // One movie (0, 5) → (0, 1): halfway between, normalized.
        QueryVectors.Blend(V(10f, 0f), [V(0f, 5f)]).ShouldBe([MathF.Sqrt(0.5f), MathF.Sqrt(0.5f)], 1e-6f);
    }

    [Fact]
    public void Blend_without_movies_returns_the_query()
    {
        QueryVectors.Blend(V(1f, 2f), []).ShouldBe([1f, 2f]);
    }

    [Fact]
    public void Blend_rejects_vectors_of_other_lengths()
    {
        Should.Throw<ArgumentException>(() => QueryVectors.Blend(V(1f, 2f), [V(1f)]));
    }

    private static ReadOnlyMemory<float> V(params float[] values) => values;
}
