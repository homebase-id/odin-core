using System.Text.Json.Serialization;
using Odin.Hosting.ApiExceptions.Client;
using NotImplementedException = System.NotImplementedException;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth;

#nullable enable

public class YouAuthTokenRequest
{
    public const string SecretDigestName = "secret_digest";
    [JsonPropertyName(SecretDigestName)]
    public string SecretDigest { get; set; } = "";

    //

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SecretDigest))
        {
            throw new BadRequestException($"{SecretDigestName} is required");
        }
    }
}