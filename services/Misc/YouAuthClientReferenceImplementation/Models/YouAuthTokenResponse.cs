// SEB:TODO
// This is a partial copy of the class in namespace Odin.Hosting.Controllers.OwnerToken.YouAuth.
// It should be in a shared lib instead.

namespace YouAuthClientReferenceImplementation.Models;

public class YouAuthTokenResponse
{
    public string? Base64SharedSecretCipher { get; set; }
    public string? Base64SharedSecretIv { get; set; }

    public string? Base64ClientAuthTokenCipher { get; set; }
    public string? Base64ClientAuthTokenIv { get; set; }
}