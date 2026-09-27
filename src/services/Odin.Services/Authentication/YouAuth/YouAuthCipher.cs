using System;

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

public static class YouAuthCipherExtensions
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

    public static string WireName(this YouAuthCipher cipher) => cipher switch
    {
        YouAuthCipher.AesCbc => YouAuthDefaults.CipherAesCbc,
        YouAuthCipher.AesGcm => YouAuthDefaults.CipherAesGcm,
        _ => throw new ArgumentOutOfRangeException(nameof(cipher), cipher, null)
    };
}
