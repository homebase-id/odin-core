#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using Odin.Core;
using Odin.Core.Exceptions;
using Gcm = Odin.Core.Cryptography.Crypto.AesGcm;

namespace Odin.Services.Certificate;

/// <summary>
/// The ACME account's private key as it is kept in the system <c>Settings</c> table: AES-GCM under
/// <c>CertificateRenewal:StorageKey</c>, the key the certificates' private keys are kept under too (#1748).
/// </summary>
/// <remarks>
/// Whoever holds the account key can revoke every certificate the account issued, so a copy of the database (a dump, a
/// backup) mustn't give it away.  It was once kept as plain PEM; <see cref="Open"/> still reads that, and says so, so the
/// caller can seal it again.
/// </remarks>
public static class AcmeAccountKeyAtRest
{
    private const string Prefix = "aes-gcm:";

    public static string Seal(string keyPem, byte[] storageKey)
    {
        var (iv, cipherText) = Gcm.Encrypt(Encoding.UTF8.GetBytes(keyPem), new SensitiveByteArray(storageKey));
        return $"{Prefix}{Convert.ToHexString(iv)}:{Convert.ToHexString(cipherText)}";
    }

    /// <summary>The key, and whether it was kept as plain PEM, from before it was sealed.</summary>
    public static (string KeyPem, bool WasPlain) Open(string stored, byte[] storageKey)
    {
        if (stored.StartsWith("-----BEGIN", StringComparison.Ordinal))
        {
            return (stored, true);
        }

        var parts = stored.StartsWith(Prefix, StringComparison.Ordinal) ? stored[Prefix.Length..].Split(':') : [];
        if (parts.Length != 2)
        {
            throw new OdinSystemException("The stored ACME account key is in no format this server knows");
        }

        try
        {
            var plain = Gcm.Decrypt(Convert.FromHexString(parts[1]), new SensitiveByteArray(storageKey), Convert.FromHexString(parts[0]));
            return (Encoding.UTF8.GetString(plain), false);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // Never treated as no account: a new one would replace the account every certificate was issued under
            throw new OdinSystemException("The stored ACME account key can't be opened with CertificateRenewal:StorageKey", e);
        }
    }
}
