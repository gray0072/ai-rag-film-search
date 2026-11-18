using FilmSearch.Api.Modules.Commerce.FakeGateway;
using FilmSearch.Core.Commerce;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FilmSearch.UnitTests.Commerce;

public sealed class FakeWebhookSignerTests
{
    private const string Body = """{"id":"evt_fake_1","type":"checkout.payment_succeeded"}""";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_signed_body_verifies()
    {
        var signer = NewSigner("secret");

        Should.NotThrow(() => signer.Verify(Body, signer.Sign(Body)));
    }

    [Fact]
    public void A_changed_body_a_wrong_key_or_a_broken_header_fails()
    {
        var signer = NewSigner("secret");
        var header = signer.Sign(Body);

        Should.Throw<WebhookSignatureException>(() => signer.Verify(Body.Replace("evt_fake_1", "evt_fake_2", StringComparison.Ordinal), header));
        Should.Throw<WebhookSignatureException>(() => NewSigner("other").Verify(Body, header));
        Should.Throw<WebhookSignatureException>(() => signer.Verify(Body, null));
        Should.Throw<WebhookSignatureException>(() => signer.Verify(Body, "v1=abc"));
        Should.Throw<WebhookSignatureException>(() => signer.Verify(Body, header.Replace("v1=", "v1=zz", StringComparison.Ordinal)));
    }

    [Fact]
    public void An_old_signature_is_rejected()
    {
        var signer = NewSigner("secret");
        var header = signer.Sign(Body);

        _time.Advance(TimeSpan.FromSeconds(301));

        Should.Throw<WebhookSignatureException>(() => signer.Verify(Body, header));
    }

    [Fact]
    public void Without_a_configured_secret_each_process_gets_a_random_key()
    {
        var first = NewSigner(null);
        var second = NewSigner(null);

        Should.Throw<WebhookSignatureException>(() => second.Verify(Body, first.Sign(Body)));
    }

    private FakeWebhookSigner NewSigner(string? secret) =>
        new(Options.Create(new FakePaymentOptions { WebhookSecret = secret, SignatureToleranceSeconds = 300 }), _time);
}
