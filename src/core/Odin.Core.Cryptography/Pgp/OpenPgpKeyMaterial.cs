using System;

namespace Odin.Core.Cryptography.Pgp;

/// <summary>
/// The two halves of a tenant's E2E email keypair, packaged as minimal OpenPGP
/// artifacts (docs/email-keys-plan.md): a P-384 ECDSA primary key with a P-384
/// ECDH encryption subkey and a single user id.
///
/// Custody: the secret keyring is owner-locked - it is stored on the encrypted
/// email drive (under the master-key/Shamir umbrella) and must never reach the
/// mail server or relay. The public certificate is what gets published (WKD,
/// DID keyAgreement) and provisioned into the mail server for encryption-at-rest.
/// </summary>
public sealed class OpenPgpKeyMaterial
{
    /// <summary>ASCII-armored transferable public key (the "certificate").</summary>
    public required string PublicCertificateArmored { get; init; }

    /// <summary>
    /// ASCII-armored secret keyring. Deliberately NOT passphrase-protected
    /// (SymmetricKeyAlgorithmTag.Null): its storage location - the encrypted
    /// email drive - is the protection layer, and a second passphrase would
    /// have to be stored right next to it.
    /// </summary>
    public required string SecretKeyArmored { get; init; }

    /// <summary>Primary key fingerprint, uppercase hex, no separators.</summary>
    public required string FingerprintHex { get; init; }

    public required DateTime CreatedUtc { get; init; }
}
