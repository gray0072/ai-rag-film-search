using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;

namespace FilmSearch.UnitTests.Commerce;

public sealed class PaymentAndEntitlementTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PaymentStatus.Pending, PaymentEventType.Succeeded, PaymentStatus.Succeeded)]
    [InlineData(PaymentStatus.Failed, PaymentEventType.Succeeded, PaymentStatus.Succeeded)]
    [InlineData(PaymentStatus.Expired, PaymentEventType.Succeeded, PaymentStatus.Succeeded)]
    [InlineData(PaymentStatus.Pending, PaymentEventType.Failed, PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Pending, PaymentEventType.Expired, PaymentStatus.Expired)]
    [InlineData(PaymentStatus.Failed, PaymentEventType.Expired, PaymentStatus.Expired)]
    [InlineData(PaymentStatus.Succeeded, PaymentEventType.Refunded, PaymentStatus.Refunded)]
    public void Payment_applies_provider_events(PaymentStatus from, PaymentEventType type, PaymentStatus to)
    {
        var payment = NewPayment(from);

        payment.Apply(type, Now).ShouldBeTrue();

        payment.Status.ShouldBe(to);
        payment.UpdatedAt.ShouldBe(Now);
    }

    [Theory]
    [InlineData(PaymentStatus.Succeeded, PaymentEventType.Succeeded)]
    [InlineData(PaymentStatus.Succeeded, PaymentEventType.Failed)]
    [InlineData(PaymentStatus.Succeeded, PaymentEventType.Expired)]
    [InlineData(PaymentStatus.Failed, PaymentEventType.Failed)]
    [InlineData(PaymentStatus.Pending, PaymentEventType.Refunded)]
    [InlineData(PaymentStatus.Refunded, PaymentEventType.Succeeded)]
    public void Payment_ignores_duplicate_and_stale_events(PaymentStatus from, PaymentEventType type)
    {
        var payment = NewPayment(from);

        payment.Apply(type, Now).ShouldBeFalse();

        payment.Status.ShouldBe(from);
    }

    [Fact]
    public void Entitlement_is_active_until_the_rental_ends_or_it_is_revoked()
    {
        var rental = new Entitlement { Type = EntitlementType.Rental, ValidFrom = Now, ValidUntil = Now.AddHours(48) };
        rental.IsActive(Now).ShouldBeTrue();
        rental.IsActive(Now.AddHours(48)).ShouldBeFalse();
        rental.IsActive(Now.AddSeconds(-1)).ShouldBeFalse();

        var owned = new Entitlement { Type = EntitlementType.Owned, ValidFrom = Now };
        owned.IsActive(Now.AddYears(10)).ShouldBeTrue();
        owned.RevokedAt = Now;
        owned.IsActive(Now).ShouldBeFalse();
    }

    [Fact]
    public void Owned_movies_cannot_be_bought_again_and_rented_ones_can_only_be_bought()
    {
        var owned = new Entitlement { Type = EntitlementType.Owned, ValidFrom = Now };
        var rental = new Entitlement { Type = EntitlementType.Rental, ValidFrom = Now, ValidUntil = Now.AddHours(1) };

        Entitlement.Blocks(OfferType.Buy, []).ShouldBeNull();
        Entitlement.Blocks(OfferType.Rent, []).ShouldBeNull();
        Entitlement.Blocks(OfferType.Buy, [owned]).ShouldBe(CommerceError.AlreadyOwned);
        Entitlement.Blocks(OfferType.Rent, [owned]).ShouldBe(CommerceError.AlreadyOwned);
        Entitlement.Blocks(OfferType.Buy, [rental]).ShouldBeNull();
        Entitlement.Blocks(OfferType.Rent, [rental]).ShouldBe(CommerceError.AlreadyRented);
    }

    private static Payment NewPayment(PaymentStatus status) => new()
    {
        ProviderSessionId = "fake_cs_1",
        Currency = "USD",
        CheckoutUrl = "/fake-checkout/fake_cs_1",
        Status = status,
        UpdatedAt = Now.AddDays(-1),
    };
}
