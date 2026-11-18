using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FilmSearch.Core.Commerce;
using Microsoft.Extensions.Options;

namespace FilmSearch.Api.Modules.Commerce.FakeGateway;

/// <summary>
/// Webhook signatures in the style of Stripe: header <c>Fake-Signature: t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256 of "t.body"&gt;</c>.
/// The timestamp is signed too, so an old delivery cannot be replayed after <see cref="FakePaymentOptions.SignatureToleranceSeconds"/>.
/// </summary>
public sealed class FakeWebhookSigner
{
    public const string HeaderName = "Fake-Signature";

    private readonly byte[] _key;
    private readonly TimeSpan _tolerance;
    private readonly TimeProvider _time;

    public FakeWebhookSigner(IOptions<FakePaymentOptions> options, TimeProvider time)
    {
        var secret = options.Value.WebhookSecret;
        _key = string.IsNullOrEmpty(secret) ? RandomNumberGenerator.GetBytes(32) : Encoding.UTF8.GetBytes(secret);
        _tolerance = TimeSpan.FromSeconds(options.Value.SignatureToleranceSeconds);
        _time = time;
    }

    public string Sign(string body)
    {
        var timestamp = _time.GetUtcNow().ToUnixTimeSeconds();
        return $"t={timestamp.ToString(CultureInfo.InvariantCulture)},v1={Convert.ToHexStringLower(Hash(timestamp, body))}";
    }

    /// <exception cref="WebhookSignatureException">Missing, malformed, wrong or expired signature.</exception>
    public void Verify(string body, string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            throw new WebhookSignatureException($"The {HeaderName} header is missing.");
        }

        long? timestamp = null;
        string? signature = null;
        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("t=", StringComparison.Ordinal) && long.TryParse(part.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var t))
            {
                timestamp = t;
            }
            else if (part.StartsWith("v1=", StringComparison.Ordinal))
            {
                signature = part[3..];
            }
        }

        if (timestamp is null || signature is null)
        {
            throw new WebhookSignatureException($"The {HeaderName} header is malformed.");
        }

        byte[] actual;
        try
        {
            actual = Convert.FromHexString(signature);
        }
        catch (FormatException)
        {
            throw new WebhookSignatureException("The signature is not hex.");
        }

        if (!CryptographicOperations.FixedTimeEquals(actual, Hash(timestamp.Value, body)))
        {
            throw new WebhookSignatureException("The signature does not match.");
        }

        var age = _time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(timestamp.Value);
        if (age.Duration() > _tolerance)
        {
            throw new WebhookSignatureException("The signature timestamp is outside the tolerance.");
        }
    }

    private byte[] Hash(long timestamp, string body) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{timestamp.ToString(CultureInfo.InvariantCulture)}.{body}"));
}
