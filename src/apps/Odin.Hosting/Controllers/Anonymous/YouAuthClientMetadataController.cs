#nullable enable
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Base;
using Odin.Services.Fingering;
using Odin.Services.Optimization.Cdn;

namespace Odin.Hosting.Controllers.Anonymous;

// Make routes in here are:
// - are accessible without authentication
// - are accessible using http

/// <summary>
/// This identity's own <c>/.well-known/youauth-client.json</c>: what it says about itself when it
/// is the relying party, which it is whenever a peer signs in on its home site. The owner's public
/// name and the public image the consent page already fetches, so the peer's consent page can name
/// this identity. No callback list: an identity host has one relying party on it, itself, so a pin
/// would protect nothing and would have to guess the port the peer sees. See
/// docs/youauth-client-metadata-plan.md.
/// </summary>
[ApiController]
[Route(YouAuthDefaults.ClientMetadataPath)]
public class YouAuthClientMetadataController(
    OdinContext context,
    StaticFileContentService staticFileContentService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var domain = context.Tenant.DomainName;

        var document = new YouAuthClientMetadataDocument
        {
            Name = await PublicNameAsync(),
            Logo = $"https://{domain}/pub/image",
        };

        // The document's own shape, not the system serializer's: the property names are the wire
        // contract and the same file is read from arbitrary sites.
        return Content(JsonSerializer.Serialize(document), "application/json", Encoding.UTF8);
    }

    /// <summary>The name on the public profile card, when one is published and carries one.</summary>
    private async Task<string?> PublicNameAsync()
    {
        var (_, fileExists, bytes) = await staticFileContentService.GetStaticFileStreamAsync(
            StaticFileConstants.PublicProfileCardFileName);

        if (!fileExists || bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            var profile = JsonSerializer.Deserialize<StaticPublicProfile>(Encoding.UTF8.GetString(bytes),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return string.IsNullOrWhiteSpace(profile?.Name) ? null : profile.Name;
        }
        catch (JsonException)
        {
            // A card that does not parse is a card with no name; the callback is still worth publishing.
            return null;
        }
    }
}
