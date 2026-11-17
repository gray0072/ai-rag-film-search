using FilmSearch.Evals.Metrics;

namespace FilmSearch.UnitTests.Evals;

public sealed class RetrievalMetricsTests
{
    private static readonly Dictionary<int, int> OneRelevant = new() { [7] = 1 };

    [Theory]
    [InlineData(new[] { 7, 1, 2 }, 1.0)]
    [InlineData(new[] { 1, 2, 7 }, 1.0 / 3)]
    [InlineData(new[] { 1, 2, 3 }, 0.0)]
    [InlineData(new int[0], 0.0)]
    public void Reciprocal_rank_is_one_over_the_rank_of_the_first_relevant_item(int[] ranked, double expected) =>
        RetrievalMetrics.ReciprocalRankAt(ranked, OneRelevant, 10).ShouldBe(expected, 1e-9);

    [Fact]
    public void Reciprocal_rank_ignores_items_below_k() =>
        RetrievalMetrics.ReciprocalRankAt([1, 2, 3, 7], OneRelevant, 3).ShouldBe(0);

    [Fact]
    public void Recall_is_hits_out_of_relevant_when_there_are_fewer_relevant_than_k()
    {
        var grades = new Dictionary<int, int> { [1] = 1, [2] = 1, [3] = 1, [4] = 1 };

        RetrievalMetrics.RecallAt([1, 9, 3, 8, 7], grades, 5).ShouldBe(0.5);
    }

    [Fact]
    public void Recall_is_capped_by_k_when_there_are_more_relevant_than_k()
    {
        var grades = Enumerable.Range(1, 20).ToDictionary(i => i, _ => 1);

        RetrievalMetrics.RecallAt([1, 2, 3, 4, 5, 100], grades, 5).ShouldBe(1.0);
    }

    [Fact]
    public void Recall_counts_a_repeated_item_once()
    {
        var grades = new Dictionary<int, int> { [1] = 1, [2] = 1 };

        RetrievalMetrics.RecallAt([1, 1, 3], grades, 3).ShouldBe(0.5);
    }

    [Fact]
    public void Ndcg_is_one_for_the_ideal_order_and_lower_for_a_worse_one()
    {
        var grades = new Dictionary<int, int> { [1] = 3, [2] = 2, [3] = 1 };

        RetrievalMetrics.NdcgAt([1, 2, 3], grades, 10).ShouldBe(1.0, 1e-9);
        RetrievalMetrics.NdcgAt([3, 2, 1], grades, 10).ShouldBeLessThan(1.0);
        RetrievalMetrics.NdcgAt([9, 8, 7], grades, 10).ShouldBe(0);
    }

    [Fact]
    public void Ndcg_uses_exponential_gain_and_log2_discount()
    {
        // Relevant item of grade 2 at rank 2, nothing at rank 1: DCG = (2^2 − 1) / log2(3); ideal = (2^2 − 1) / log2(2) = 3.
        var grades = new Dictionary<int, int> { [5] = 2 };

        RetrievalMetrics.NdcgAt([9, 5], grades, 10).ShouldBe(1 / Math.Log2(3), 1e-9);
    }

    [Fact]
    public void Ndcg_ideal_order_is_cut_at_k()
    {
        // Two relevant items, k = 1: the best possible top 1 holds one of them.
        var grades = new Dictionary<int, int> { [1] = 1, [2] = 1 };

        RetrievalMetrics.NdcgAt([2, 1], grades, 1).ShouldBe(1.0, 1e-9);
    }

    [Theory]
    [InlineData(50, 2.5)]
    [InlineData(0, 1.0)]
    [InlineData(100, 4.0)]
    [InlineData(95, 3.85)]
    public void Percentile_interpolates_between_ranks(double percentile, double expected) =>
        RetrievalMetrics.Percentile([4, 1, 3, 2], percentile).ShouldBe(expected, 1e-9);

    [Fact]
    public void Percentile_of_no_values_is_zero() =>
        RetrievalMetrics.Percentile([], 50).ShouldBe(0);
}
