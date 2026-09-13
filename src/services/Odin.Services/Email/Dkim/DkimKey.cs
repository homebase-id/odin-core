using System;

namespace Odin.Services.Email.Dkim;

public enum DkimAlgorithm
{
    Ed25519, // RFC 8463; DNS k=ed25519, p= is the raw 32-byte public key
    Rsa,     // rsa-sha256, 2048-bit; DNS k=rsa, p= is the SubjectPublicKeyInfo DER
}

/// <summary>
/// One DKIM signing keypair for a tenant domain (docs/email-keys-plan.md,
/// docs/email-dns-plan.md). Two exist per activated tenant: s1 (ed25519) and
/// s2 (rsa-2048) - modern receivers verify s1, legacy receivers s2, and either
/// selector can be rotated independently.
///
/// Custody is server-operational: DKIM keys are disposable (lost = rotate + new
/// TXT record), so they are stored TLS-key-style via <see cref="IDkimStore"/>,
/// never on the owner-locked email drive.
/// </summary>
public sealed class DkimKey
{
    public required string Selector { get; init; }

    public required DkimAlgorithm Algorithm { get; init; }

    /// <summary>Ed25519: raw 32-byte public key. Rsa: SubjectPublicKeyInfo DER.</summary>
    public required byte[] PublicKey { get; init; }

    /// <summary>PKCS#8 DER private key. Plaintext only in memory - <see cref="IDkimStore"/> encrypts at rest.</summary>
    public required byte[] PrivateKeyPkcs8 { get; init; }

    /// <summary>The DKIM k= tag value, doubling as the stored algorithm discriminator.</summary>
    public string KTag => Algorithm == DkimAlgorithm.Ed25519 ? "ed25519" : "rsa";

    /// <summary>The p= tag value: base64 of <see cref="PublicKey"/>.</summary>
    public string PublicKeyBase64 => Convert.ToBase64String(PublicKey);

    /// <summary>Record owner name relative to the tenant domain, e.g. "s1._domainkey".</summary>
    public string DnsRecordName => $"{Selector}._domainkey";

    /// <summary>The TXT record content receivers look up.</summary>
    public string DnsRecordValue => $"v=DKIM1; k={KTag}; p={PublicKeyBase64}";

    public static DkimAlgorithm AlgorithmFromKTag(string kTag)
    {
        return kTag switch
        {
            "ed25519" => DkimAlgorithm.Ed25519,
            "rsa" => DkimAlgorithm.Rsa,
            _ => throw new ArgumentException($"Unknown DKIM k= tag '{kTag}'", nameof(kTag)),
        };
    }
}
