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
        ArgumentException.ThrowIfNullOrEmpty(context);
        using var key = new SensitiveByteArray(storageKey);
        var plain = Encoding.UTF8.GetBytes(secret);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var sealedBytes = AesGcm.Encrypt(plain, key, nonce, Encoding.UTF8.GetBytes(context));
            return $"{Prefix}{Convert.ToHexString(nonce)}:{Convert.ToHexString(sealedBytes)}";
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    /// <summary>
    /// The secret; a value in no known scheme, one the key doesn't open, or one sealed for another context, is an error
    /// that names the context.
    /// </summary>
    public static string Open(string stored, byte[] storageKey, string context)
    {
        ArgumentException.ThrowIfNullOrEmpty(context);
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new OdinSystemException($"The stored secret {context} is in no scheme this server knows");
        }

        using var key = new SensitiveByteArray(storageKey);
        byte[]? plain = null;
        try
        {
            var parts = stored[Prefix.Length..].Split(':', 2);
            var nonce = Convert.FromHexString(parts[0]);
            var sealedBytes = Convert.FromHexString(parts[1]);
            if (nonce.Length != NonceSize || sealedBytes.Length < TagSize)
            {
                throw new FormatException("A sealed secret has a 12-byte nonce and a 16-byte tag");
            }

            plain = AesGcm.Decrypt(sealedBytes, key, nonce, Encoding.UTF8.GetBytes(context));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or IndexOutOfRangeException)
        {
            throw new OdinSystemException($"The stored secret {context} can't be opened with this storage key", e);
        }
        finally
        {
            if (plain != null)
            {
                Array.Clear(plain);
            }
        }
    }
}
