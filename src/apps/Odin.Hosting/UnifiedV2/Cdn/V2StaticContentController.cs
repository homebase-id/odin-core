using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base.Cdn;
using Odin.Hosting.Controllers.OwnerToken.Cdn;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Optimization.Cdn;

namespace Odin.Hosting.UnifiedV2.Cdn;

/// <summary>
/// The V2 twin of V1's <c>optimization/cdn</c>. <c>publish</c> (the base class) is on the owner and on apps, as on V1,
/// and needs PublishStaticContent. The profile card and image were never on the app controller, and their service
/// takes no context and checks nothing, so these routes are the only check: owner console only.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.StaticContent)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2StaticContentController(StaticFileContentService staticFileContentService)
    : StaticFileContentPublishControllerBase(staticFileContentService)
{
    private readonly StaticFileContentService _staticFileContentService = staticFileContentService;

    [HttpPost("profile-image")]
    [UnifiedV2Authorize(UnifiedPolicies.Owner)]
    public async Task<IActionResult> PublishPublicProfileImage([FromBody] PublishPublicProfileImageRequest request)
    {
        WebOdinContext.Caller.AssertHasMasterKey();

        if (string.IsNullOrEmpty(request.Image64))
        {
            return BadRequest("Image64 is required");
        }

        if (request.Image64.Length > Constants.OneMiB)
        {
            return BadRequest("Image64 is too big (> 1 MiB)");
        }

        await _staticFileContentService.PublishProfileImageAsync(request.Image64, request.ContentType);
        return NoContent();
    }

    [HttpPost("profile-card")]
    [UnifiedV2Authorize(UnifiedPolicies.Owner)]
    public async Task<IActionResult> PublishPublicProfileCard([FromBody] PublishPublicProfileCardRequest request)
    {
        WebOdinContext.Caller.AssertHasMasterKey();

        await _staticFileContentService.PublishProfileCardAsync(request.ProfileCardJson);
        return NoContent();
    }
}
