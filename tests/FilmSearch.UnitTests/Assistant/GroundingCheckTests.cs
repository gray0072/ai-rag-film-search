using FilmSearch.AI.Assistant;
using FilmSearch.Core.Assistant;
using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;

namespace FilmSearch.UnitTests.Assistant;

public sealed class GroundingCheckTests
{
    [Fact]
    public void Bold_and_quoted_titles_are_candidates_labels_and_prices_are_not()
    {
        const string answer = """
            Here you go:
            - **Interstellar** (2014) — space and time.
            - **Солярис (1972)**: классика.
            - «Амели» и “La La Land”, och ”Fucking Åmål”.
            **Why it fits:** **Total: 12.99 USD** **Price**
            """;

        GroundingCheck.Candidates(answer).ShouldBe(["Interstellar", "Солярис", "Амели", "La La Land", "Fucking Åmål", "Price"]);
    }

    [Fact]
    public void Titles_are_matched_after_normalization_as_whole_words()
    {
        string[] references =
        [
            """{"movies":[{"id":"1","title":"Ёлки","year":2010},{"id":"2","title":"Amélie","year":2001},{"id":"3","title":"Star Wars: A New Hope"}]}""",
            "We liked Before Sunrise.",
        ];

        var result = GroundingCheck.Check("**Елки**, **AMELIE**, **Star Wars - A New Hope**, **Before Sunrise**, **Star**, **Wars of the Worlds**", references);

        result.Titles.Count.ShouldBe(6);
        result.Unknown.ShouldBe(["Wars of the Worlds"]); // "Star" is a whole word of a known title: the heuristic accepts it
    }

    [Fact]
    public void Inflected_russian_titles_are_grounded_and_lower_case_emphasis_is_not_a_title()
    {
        string[] references = ["""{"movies":[{"title":"Матрица"},{"title":"Ла-Ла Ленд"},{"title":"Interstellar"}]}"""];

        var result = GroundingCheck.Check(
            "Похоже на **Матрицу** и **Матрицей**, как в «Ла-Ла Ленде»; **детская** версия; **148 minutes**, **2 ч 55 мин**; **Interstellars**; **Мастер и Маргарита**",
            references);

        result.Titles.ShouldBe(["Матрицу", "Матрицей", "Ла-Ла Ленде", "2 ч 55 мин", "Interstellars", "Мастер и Маргарита"]);
        result.Unknown.ShouldBe(["2 ч 55 мин", "Interstellars", "Мастер и Маргарита"]); // stems only for Cyrillic words
    }

    [Fact]
    public void Normalization_drops_case_diacritics_and_punctuation()
    {
        GroundingCheck.Normalize("  Ёлки-2: Amélie's  Fabulous (Destiny) ").ShouldBe("елки 2 amelie s fabulous destiny");
    }

    [Fact]
    public void Cart_fingerprint_ignores_line_order_and_titles_but_not_prices_or_availability()
    {
        var a = Line(Guid.NewGuid(), 999);
        var b = Line(Guid.NewGuid(), 399);
        var cart = new CartView([a, b], 1398, "USD");

        var hash = CartFingerprint.Compute(cart);

        hash.Length.ShouldBe(64);
        CartFingerprint.Compute(new CartView([b with { Title = "Другое название" }, a], 1398, "USD")).ShouldBe(hash);
        CartFingerprint.Compute(new CartView([a, b with { PriceMinor = 499 }], 1498, "USD")).ShouldNotBe(hash);
        CartFingerprint.Compute(new CartView([a, b with { Available = false }], 999, "USD")).ShouldNotBe(hash);
        CartFingerprint.Compute(new CartView([a], 999, "USD")).ShouldNotBe(hash);
    }

    [Fact]
    public void A_pending_confirmation_past_its_expiry_time_is_expired()
    {
        var now = DateTimeOffset.UtcNow;
        var confirmation = new CheckoutConfirmation { CartHash = "x", SummaryJson = "{}", Status = ConfirmationStatus.Pending, ExpiresAt = now };

        confirmation.StatusAt(now.AddSeconds(-1)).ShouldBe(ConfirmationStatus.Pending);
        confirmation.StatusAt(now).ShouldBe(ConfirmationStatus.Expired);
        confirmation.Status = ConfirmationStatus.Approved;
        confirmation.StatusAt(now.AddHours(1)).ShouldBe(ConfirmationStatus.Approved);
    }

    private static CartLine Line(Guid movieId, long price) =>
        new(movieId, "Title", 2000, null, Guid.NewGuid(), OfferType.Buy, price, "USD", null, CartItemSource.User, DateTimeOffset.UnixEpoch, true);
}
