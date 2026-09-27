using System.Text.Json;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Optimization.Cdn;

namespace Odin.Services.Fingering;

#nullable enable

/// <summary>
/// This identity's own <c>/.well-known/youauth-client.json</c>: what it says about itself when it
/// is the relying party, which it is whenever a peer signs in on its home site. The owner's public
/// name, so the peer's consent page can say what this identity calls itself. No callback list: an
/// identity host has one relying party on it, itself. See docs/youauth-client-metadata-plan.md.
/// </summary>
public interface IOwnClientMetadataService
{
    Task<YouAuthClientMetadataDocument> GetAsync();
}

public class OwnClientMetadataService(StaticFileContentService staticFileContentService) : IOwnClientMetadataService
{
    public async Task<YouAuthClientMetadataDocument> GetAsync()
    {
        return new YouAuthClientMetadataDocument
        {
            Name = await PublicNameAsync(),
        };
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
            var profile = OdinSystemSerializer.Deserialize<StaticPublicProfile>(bytes);
            return string.IsNullOrWhiteSpace(profile?.Name) ? null : profile.Name;
        }
        catch (JsonException)
        {
            // A card that does not parse is a card with no name; the document is still worth serving.
            return null;
        }
    }
}
