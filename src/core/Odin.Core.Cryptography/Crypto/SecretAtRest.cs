#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using Odin.Core.Exceptions;

namespace Odin.Core.Cryptography.Crypto;

/// <summary>
/// A secret kept at rest under a storage key: AES-GCM with a fresh 12-byte nonce, bound to a context (what the secret is,
/// e.g. the row it's kept in) as associated data, so it opens only where it was sealed.  Kept as
/// <c>aes-gcm:&lt;nonce hex&gt;:&lt;ciphertext and tag hex&gt;</c>; the prefix names the scheme, so a later one can sit
/// beside it.  First used for the ACME account key (#1748); the certificate and DKIM keys still use AES-CBC with a derived
/// IV (#1911).
/// </summary>
public static class SecretAtRest
{
    private const string Prefix = "aes-gcm:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static string Seal(string secret, byte[] storageKey, string context)
    {
        var plain = Encoding.UTF8.GetBytes(secret);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var sealedBytes = new byte[plain.Length + TagSize];
            using var gcm = new System.Security.Cryptography.AesGcm(storageKey, TagSize);
            gcm.Encrypt(nonce, plain, sealedBytes.AsSpan(0, plain.Length), sealedBytes.AsSpan(plain.Length),
                Encoding.UTF8.GetBytes(context));
            return $"{Prefix}{Convert.ToHexString(nonce)}:{Convert.ToHexString(sealedBytes)}";
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    /// <summary>
    /// The secret; a value in no known scheme, one the key doesn't open, or one sealed for another context, is an error.
    /// </summary>
    public static string Open(string stored, byte[] storageKey, string context)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new OdinSystemException("The stored secret is in no scheme this server knows");
        }

        try
        {
            var parts = stored[Prefix.Length..].Split(':', 2);
            var nonce = Convert.FromHexString(parts[0]);
            var sealedBytes = Convert.FromHexString(parts[1]);
            if (nonce.Length != NonceSize || sealedBytes.Length < TagSize)
            {
                throw new FormatException("A sealed secret has a 12-byte nonce and a 16-byte tag");
            }

            var plain = new byte[sealedBytes.Length - TagSize];
            using var gcm = new System.Security.Cryptography.AesGcm(storageKey, TagSize);
            gcm.Decrypt(nonce, sealedBytes.AsSpan(0, plain.Length), sealedBytes.AsSpan(plain.Length), plain,
                Encoding.UTF8.GetBytes(context));
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
