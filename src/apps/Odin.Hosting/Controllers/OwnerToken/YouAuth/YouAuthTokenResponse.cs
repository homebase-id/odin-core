using System;
using Odin.Services.Authentication.YouAuth;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth;

#nullable enable

public class YouAuthTokenResponse
{
    public string? Base64SharedSecretCipher { get; set; }
    public string? Base64SharedSecretIv { get; set; }

    public string? Base64ClientAuthTokenCipher { get; set; }
    public string? Base64ClientAuthTokenIv { get; set; }

    /// <summary>
    /// Which cipher sealed the two fields above: <c>aes-cbc</c> or <c>aes-gcm</c>. Nullable because
    /// this DTO also reads a peer's response, and a peer that predates the field sends none (CBC).
    /// </summary>
    public string? Cipher { get; set; }

    /// <summary>YouAuth [140]: what the identity cached at [070], base64, with the cipher that sealed it.</summary>
    public static YouAuthTokenResponse From(EncryptedTokenExchange exchange) => new()
    {
        Base64SharedSecretCipher = Convert.ToBase64String(exchange.SharedSecretCipher),
        Base64SharedSecretIv = Convert.ToBase64String(exchange.SharedSecretIv),
        Base64ClientAuthTokenCipher = Convert.ToBase64String(exchange.ClientAuthTokenCipher),
        Base64ClientAuthTokenIv = Convert.ToBase64String(exchange.ClientAuthTokenIv),
        Cipher = exchange.Cipher.WireName(),
    };
}