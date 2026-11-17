using FilmSearch.Infrastructure.Search.Hybrid;

namespace FilmSearch.UnitTests.Search;

public sealed class ReciprocalRankFusionTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    [Fact]
    public void Score_is_the_sum_of_weighted_reciprocal_ranks()
    {
        var fused = ReciprocalRankFusion.Fuse([new([A, B, C]), new([C, A], Weight: 0.5)], k: 60);

        var a = fused.Single(h => h.MovieId == A);
        a.Score.ShouldBe((1.0 / 61) + (0.5 / 62), 1e-12);
        a.Ranks.ShouldBe([1, 2]);

        var b = fused.Single(h => h.MovieId == B);
        b.Score.ShouldBe(1.0 / 62, 1e-12);
        b.Ranks.ShouldBe([2, null]);

        fused.Single(h => h.MovieId == C).Score.ShouldBe((1.0 / 63) + (0.5 / 61), 1e-12);
    }

    [Fact]
    public void A_movie_found_by_both_lists_beats_a_top_hit_of_one_list()
    {
        // D is #1 in keyword only; A is #5 in both. 2/65 > 1/61.
        var keyword = new RankedList([D, B, C, Guid.NewGuid(), A]);
        var semantic = new RankedList([Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), A]);

        var fused = ReciprocalRankFusion.Fuse([keyword, semantic]);

        fused[0].MovieId.ShouldBe(A);
        fused[1].MovieId.ShouldBe(D);
    }

    [Fact]
    public void Smaller_k_trusts_the_top_of_each_list_more()
    {
        var keyword = new RankedList([D, B, C, Guid.NewGuid(), A]);
        var semantic = new RankedList([Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), A]);

        // k = 1: 1/2 for rank 1 vs 2/6 for rank 5 twice.
        ReciprocalRankFusion.Fuse([keyword, semantic], k: 1)[0].MovieId.ShouldBe(D);
    }

    [Fact]
    public void Weight_zero_ignores_a_list_and_ties_are_deterministic()
    {
        var fused = ReciprocalRankFusion.Fuse([new([B, A]), new([A, B], Weight: 0)]);

        fused.Select(h => h.MovieId).ShouldBe([B, A]);

        // Equal scores (each movie is #1 in one list): the movie id decides.
        ReciprocalRankFusion.Fuse([new([B]), new([A])]).Select(h => h.MovieId).ShouldBe([A, B]);
    }

    [Fact]
    public void Single_list_keeps_its_order_and_empty_input_gives_nothing()
    {
        ReciprocalRankFusion.Fuse([new([C, A, B])]).Select(h => h.MovieId).ShouldBe([C, A, B]);
        ReciprocalRankFusion.Fuse([]).ShouldBeEmpty();
        ReciprocalRankFusion.Fuse([new([]), new([])]).ShouldBeEmpty();
    }

    [Fact]
    public void A_repeated_movie_counts_once_at_its_best_rank()
    {
        var fused = ReciprocalRankFusion.Fuse([new([A, B, A])], k: 60);

        fused.Count.ShouldBe(2);
        fused.Single(h => h.MovieId == A).Score.ShouldBe(1.0 / 61, 1e-12);
    }
}
