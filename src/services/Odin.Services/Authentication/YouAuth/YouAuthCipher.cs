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
    /// <summary>The wire value: null or empty is CBC, what every client got before the parameter existed.</summary>
    public static bool TryParse(string? wireName, out YouAuthCipher cipher)
    {
        cipher = YouAuthCipher.AesCbc;
        return false;
    }

    public static string WireName(this YouAuthCipher cipher) => "";
}
