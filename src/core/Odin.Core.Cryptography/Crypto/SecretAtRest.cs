#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using Odin.Core.Exceptions;

namespace Odin.Core.Cryptography.Crypto;

/// <summary>
/// A secret kept at rest under a storage key: AES-GCM with a fresh IV, as <c>aes-gcm:&lt;iv hex&gt;:&lt;ciphertext hex&gt;</c>.
/// The prefix names the scheme, so a later one can sit beside it.  First used for the ACME account key (#1748); the
/// certificate and DKIM keys still use AES-CBC with a derived IV (#1911).
/// </summary>
public static class SecretAtRest
{
    private const string Prefix = "aes-gcm:";

    public static string Seal(string secret, byte[] storageKey)
    {
        var (iv, cipherText) = AesGcm.Encrypt(Encoding.UTF8.GetBytes(secret), new SensitiveByteArray(storageKey));
        return $"{Prefix}{Convert.ToHexString(iv)}:{Convert.ToHexString(cipherText)}";
    }

    /// <summary>The secret; a value in no known scheme, or one the key doesn't open, is an error.</summary>
    public static string Open(string stored, byte[] storageKey)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new OdinSystemException("The stored secret is in no scheme this server knows");
        }

        try
        {
            var parts = stored[Prefix.Length..].Split(':', 2);
            var plain = AesGcm.Decrypt(Convert.FromHexString(parts[1]), new SensitiveByteArray(storageKey), Convert.FromHexString(parts[0]));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or IndexOutOfRangeException)
        {
            throw new OdinSystemException("The stored secret can't be opened with this storage key", e);
        }
    }
}
