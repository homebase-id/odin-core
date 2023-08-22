using System;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth;

#nullable enable

public class YouAuthTokenResponse
{
    [Obsolete("SEB:TODO delete me")]
    public string? Base64SharedSecret { get; set; }
    [Obsolete("SEB:TODO delete me")]
    public string? Base64ClientAuthToken { get; set; }

    public string? Base64SharedSecretCipher { get; set; }
    public string? Base64SharedSecretIv { get; set; }

    public string? Base64ClientAuthTokenCipher { get; set; }
    public string? Base64ClientAuthTokenIv { get; set; }
}