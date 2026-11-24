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
        using var key = new SensitiveByteArray(storageKey);
        var plain = Encoding.UTF8.GetBytes(secret);
        try
        {
            var (iv, cipherText) = AesGcm.Encrypt(plain, key);
            return $"{Prefix}{Convert.ToHexString(iv)}:{Convert.ToHexString(cipherText)}";
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    /// <summary>The secret; a value in no known scheme, or one the key doesn't open, is an error.</summary>
    public static string Open(string stored, byte[] storageKey)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new OdinSystemException("The stored secret is in no scheme this server knows");
        }

        using var key = new SensitiveByteArray(storageKey);
        try
        {
            var parts = stored[Prefix.Length..].Split(':', 2);
            var plain = AesGcm.Decrypt(Convert.FromHexString(parts[1]), key, Convert.FromHexString(parts[0]));
            var secret = Encoding.UTF8.GetString(plain);
            Array.Clear(plain);
            return secret;
        }
        catch (Exception e) when (e is CryptographicException or FormatException or IndexOutOfRangeException or ArgumentException)
        {
            throw new OdinSystemException("The stored secret can't be opened with this storage key", e);
        }
    }
}
