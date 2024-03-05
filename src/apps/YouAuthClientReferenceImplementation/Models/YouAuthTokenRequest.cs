using System.Text.Json.Serialization;

// SEB:TODO
// This is a partial copy of the class in namespace Odin.Hosting.Controllers.OwnerToken.YouAuth.
// It should be in a shared lib instead.

namespace YouAuthClientReferenceImplementation.Models;

public class YouAuthTokenRequest
{
    public const string SecretDigestName = "secret_digest";
    [JsonPropertyName(SecretDigestName)]
    public string SecretDigest { get; set; } = "";
}
