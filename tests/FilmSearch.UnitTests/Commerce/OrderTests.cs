using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;

namespace FilmSearch.UnitTests.Commerce;

/// <summary>Every transition of the order state machine (spec 03 §2) and the invalid ones.</summary>
public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);
    private static readonly Guid Owner = Guid.NewGuid();

    private static readonly OrderItemDraft Bought = new(Guid.NewGuid(), Guid.NewGuid(), OfferType.Buy, "Alien", 999, "USD", null);
    private static readonly OrderItemDraft Rented = new(Guid.NewGuid(), Guid.NewGuid(), OfferType.Rent, "Heat", 399, "USD", 48);

    [Fact]
    public void Create_takes_a_price_snapshot_and_waits_for_payment()
    {
        var order = NewOrder();

        order.Status.ShouldBe(OrderStatus.PendingPayment);
        order.TotalMinor.ShouldBe(1398);
        order.Currency.ShouldBe("USD");
        order.ExpiresAt.ShouldBe(Now + Timeout);
        order.PaidAt.ShouldBeNull();
        order.Entitlements.ShouldBeEmpty();
        order.Items.Select(i => (i.TitleSnapshot, i.PriceMinorSnapshot, i.RentalHours))
            .ShouldBe([("Alien", 999L, (int?)null), ("Heat", 399L, (int?)48)]);
    }

    [Fact]
    public void Create_rejects_invalid_items()
    {
        Should.Throw<ArgumentException>(() => NewOrder([]));
        Should.Throw<ArgumentException>(() => NewOrder([Bought, Bought with { OfferType = OfferType.Rent }]));
        Should.Throw<ArgumentException>(() => NewOrder([Bought, Rented with { Currency = "EUR" }]));
        Should.Throw<ArgumentException>(() => NewOrder([Bought with { PriceMinor = -1 }]));
    }

    [Fact]
    public void Pending_to_paid_grants_owned_and_rental_entitlements()
    {
        var order = NewOrder();
        var paidAt = Now.AddMinutes(5);

        order.MarkPaid(paidAt, Guid.NewGuid).ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Paid);
        order.PaidAt.ShouldBe(paidAt);
        var owned = order.Entitlements.Single(e => e.MovieId == Bought.MovieId);
        owned.Type.ShouldBe(EntitlementType.Owned);
        owned.ValidUntil.ShouldBeNull();
        var rental = order.Entitlements.Single(e => e.MovieId == Rented.MovieId);
        rental.Type.ShouldBe(EntitlementType.Rental);
        rental.ValidUntil.ShouldBe(paidAt.AddHours(48));
        order.Entitlements.ShouldAllBe(e => e.OwnerId == Owner && e.OrderId == order.Id && e.ValidFrom == paidAt);
    }

    [Fact]
    public void Paid_again_is_a_duplicate_and_grants_nothing_more()
    {
        var order = NewOrder();
        order.MarkPaid(Now, Guid.NewGuid);

        order.MarkPaid(Now.AddMinutes(1), Guid.NewGuid).ShouldBeFalse();

        order.Entitlements.Count.ShouldBe(2);
        order.PaidAt.ShouldBe(Now);
    }

    [Fact]
    public void Pending_to_failed_and_retry_back_to_pending_with_a_new_expiry()
    {
        var order = NewOrder();

        order.MarkFailed().ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Failed);

        var retryAt = Now.AddMinutes(20);
        order.RetryPayment(retryAt, Timeout).ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.PendingPayment);
        order.ExpiresAt.ShouldBe(retryAt + Timeout);
    }

    [Fact]
    public void Failed_to_paid_when_a_retry_on_the_provider_page_succeeds()
    {
        var order = NewOrder();
        order.MarkFailed();

        order.MarkPaid(Now, Guid.NewGuid).ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public void Late_success_after_expiry_still_marks_paid()
    {
        var order = NewOrder();
        order.Expire(Now + Timeout).ShouldBeTrue();

        order.MarkPaid(Now + Timeout + TimeSpan.FromMinutes(1), Guid.NewGuid).ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Paid);
        order.Entitlements.Count.ShouldBe(2);
    }

    [Fact]
    public void A_failure_after_success_or_expiry_is_stale()
    {
        var paid = NewOrder();
        paid.MarkPaid(Now, Guid.NewGuid);
        paid.MarkFailed().ShouldBeFalse();
        paid.Status.ShouldBe(OrderStatus.Paid);

        var expired = NewOrder();
        expired.Expire(Now + Timeout);
        expired.MarkFailed().ShouldBeFalse();
        expired.Status.ShouldBe(OrderStatus.Expired);

        var failed = NewOrder();
        failed.MarkFailed();
        failed.MarkFailed().ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unpaid_orders_expire_at_their_expiry_time(bool failedFirst)
    {
        var order = NewOrder();
        if (failedFirst)
        {
            order.MarkFailed();
        }

        Should.Throw<OrderStateException>(() => order.Expire(Now + Timeout - TimeSpan.FromSeconds(1)));
        order.Expire(Now + Timeout).ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Expired);
        order.Expire(Now + Timeout).ShouldBeFalse();
    }

    [Fact]
    public void Paid_orders_do_not_expire()
    {
        var order = NewOrder();
        order.MarkPaid(Now, Guid.NewGuid);

        order.Expire(Now.AddDays(1)).ShouldBeFalse();

        order.Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public void Checkout_is_possible_only_for_unpaid_orders_before_expiry()
    {
        var order = NewOrder();
        order.CanStartCheckout(Now).ShouldBeTrue();
        order.CanStartCheckout(Now + Timeout).ShouldBeFalse();
        order.RetryPayment(Now, Timeout).ShouldBeFalse(); // pending: nothing to retry

        Should.Throw<OrderStateException>(() => order.RetryPayment(Now + Timeout, Timeout));

        order.MarkPaid(Now, Guid.NewGuid);
        order.CanStartCheckout(Now).ShouldBeFalse();
        Should.Throw<OrderStateException>(() => order.RetryPayment(Now, Timeout));

        var expired = NewOrder();
        expired.Expire(Now + Timeout);
        Should.Throw<OrderStateException>(() => expired.RetryPayment(Now, Timeout));
    }

    [Fact]
    public void Refund_revokes_entitlements_only_for_paid_orders()
    {
        var pending = NewOrder();
        pending.MarkRefunded(Now).ShouldBeFalse();
        pending.Status.ShouldBe(OrderStatus.PendingPayment);

        var order = NewOrder();
        order.MarkPaid(Now, Guid.NewGuid);
        var refundedAt = Now.AddDays(1);

        order.MarkRefunded(refundedAt).ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Refunded);
        order.Entitlements.ShouldAllBe(e => e.RevokedAt == refundedAt);
        order.MarkRefunded(refundedAt).ShouldBeFalse();
        order.MarkPaid(refundedAt, Guid.NewGuid).ShouldBeFalse();
    }

    private static Order NewOrder(IReadOnlyList<OrderItemDraft>? items = null) =>
        Order.Create(Guid.NewGuid(), Owner, items ?? [Bought, Rented], OrderSource.Web, null, "key-1", Now, Timeout);
}
