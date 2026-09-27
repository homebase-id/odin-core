using System;

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
}