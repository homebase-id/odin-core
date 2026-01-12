#nullable enable
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Cdn;

// SEB:NOTE this controller is currently only used for CDN testing

[ApiController]
[Route(UnifiedApiRouteConstants.DrivesRoot)]
[UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2CdnController : OdinControllerBase
{
    [HttpGet("cdn-ping/payload/cdn-ping")]
    [SwaggerOperation(Tags = [SwaggerInfo.Auth])]
    public ActionResult CdnPing()
    {
        return Ok();
    }

    [HttpGet("cdn-ping/bad-cdn-path/cdn-ping")]
    [SwaggerOperation(Tags = [SwaggerInfo.Auth])]
    public ActionResult CdnPingBadPath()
    {
        // SEB:NOTE this will never happen since CdnAuthPathHandler will reject the path before it reaches here
        return Ok();
    }
}
