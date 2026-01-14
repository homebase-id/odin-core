#nullable enable
using System;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;

namespace Odin.Hosting.UnifiedV2.Cdn;

// SEB:NOTE this controller is currently only used for CDN testing

[ApiController]
[Route(UnifiedApiRouteConstants.DrivesRoot)]
[UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2CdnController : OdinControllerBase
{
    [HttpGet("cdn-ping/payload/{size:int}")]
    public ActionResult<string> CdnPing(int size)
    {
        var bytes = new byte[size];
        bytes.AsSpan().Fill((byte)'X');
        return new FileStreamResult(bytes.ToMemoryStream(), "application/octet-stream"); // simulate payload response
    }

    [HttpGet("cdn-ping/bad-cdn-path")]
    public ActionResult<string> CdnPingBadPath()
    {
        // SEB:NOTE this will never happen since CdnAuthPathHandler will reject the path before it reaches here
        return Ok("pong");
    }
}
