namespace Odin.Hosting.Controllers.OwnerToken.YouAuth;

#nullable enable

public class YouAuthTokenResponse
{
    public string? Base64SharedSecret { get; set; }
    public string? Base64ClientAuthToken { get; set; }
}