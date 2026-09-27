using System;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using AesGcm = Odin.Core.Cryptography.Crypto.AesGcm;

namespace Odin.Services.Authentication.YouAuth;

#nullable enable

/// <summary>
/// The cipher a relying party asked for at YouAuth [030] and the token response is sealed with at
/// [140]. Stored in the cache entry between the two, so the numeric values are part of what a
/// rolling deploy reads back: 0 must stay CBC, because an entry written by a node that predates
/// the field deserializes with no value at all.
/// </summary>
public enum YouAuthCipher
{
    AesCbc = 0,
    AesGcm = 1,
}

/// <summary>
/// The one place that knows what each <see cref="YouAuthCipher"/> means: its wire name, and which
/// primitive it is. Both use the 16-byte exchange secret as the AES-128 key. CBC is the padded,
/// unauthenticated bytes every client got before the choice existed. GCM's IV is 16 bytes of which
/// the first 12 are the nonce, and its ciphertext carries the 16-byte tag at the end; no associated
/// data. See docs/youauth-unified-authorization.md, "token endpoint".
/// </summary>
public static class YouAuthCiphers
{
    /// <summary>
    /// The wire value to the cipher. Null or empty is CBC, what every client got before the
    /// parameter existed; anything else the identity does not know is false.
    /// </summary>
    public static bool TryParse(string? wireName, out YouAuthCipher cipher)
    {
        switch (wireName)
        {
            case null or "" or YouAuthDefaults.CipherAesCbc:
                cipher = YouAuthCipher.AesCbc;
                return true;
            case YouAuthDefaults.CipherAesGcm:
                cipher = YouAuthCipher.AesGcm;
                return true;
            default:
                cipher = YouAuthCipher.AesCbc;
                return false;
        }
    }

    /// <summary><see cref="TryParse"/>, or an <see cref="ArgumentException"/> naming the accepted values.</summary>
    public static YouAuthCipher Parse(string? wireName)
    {
        return TryParse(wireName, out var cipher)
            ? cipher
            : throw new ArgumentException($"Unknown {YouAuthDefaults.Cipher} '{wireName}': {YouAuthDefaults.CipherAesCbc} or {YouAuthDefaults.CipherAesGcm}");
    }

    public static string WireName(this YouAuthCipher cipher) => cipher switch
    {
        YouAuthCipher.AesCbc => YouAuthDefaults.CipherAesCbc,
        YouAuthCipher.AesGcm => YouAuthDefaults.CipherAesGcm,
        _ => throw new ArgumentOutOfRangeException(nameof(cipher), cipher, null)
    };

    public static (byte[] iv, byte[] cipherText) Seal(this YouAuthCipher cipher, byte[] plain, SensitiveByteArray key) => cipher switch
    {
        YouAuthCipher.AesCbc => AesCbc.Encrypt(plain, key),
        YouAuthCipher.AesGcm => AesGcm.Encrypt(plain, key),
        _ => throw new ArgumentOutOfRangeException(nameof(cipher), cipher, null)
    };

    public static byte[] Open(this YouAuthCipher cipher, byte[] cipherText, SensitiveByteArray key, byte[] iv) => cipher switch
    {
        YouAuthCipher.AesCbc => AesCbc.Decrypt(cipherText, key, iv),
        YouAuthCipher.AesGcm => AesGcm.Decrypt(cipherText, key, iv),
        _ => throw new ArgumentOutOfRangeException(nameof(cipher), cipher, null)
    };
}
