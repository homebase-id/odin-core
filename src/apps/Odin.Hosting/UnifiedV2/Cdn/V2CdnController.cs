#nullable enable
using System;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Configuration;

namespace Odin.Hosting.UnifiedV2.Cdn;

// SEB:NOTE this controller is currently only used for CDN testing

[ApiController]
[Route(UnifiedApiRouteConstants.DrivesRoot)]
[UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2CdnController(OdinConfiguration config) : OdinControllerBase
{
    [HttpGet("cdn-ping/payload/{size:int}")]
    public ActionResult<string> CdnPing(int size)
    {
        if (!config.Cdn.Enabled)
        {
            return Conflict("CDN is disabled");
        }

        // SEB:NOTE manual parsing of and checking of bearer token. This is strictly for testing purposes.

        var authorization = Request.Headers[HeaderNames.Authorization].ToString();
        var parts = Regex.Split(authorization, "bearer ", RegexOptions.IgnoreCase);
        if (parts.Length != 2)
        {
            return Unauthorized("Missing or invalid Authorization header");
        }

        if (parts[1] != config.Cdn.ExpectedAuthToken.ToString())
        {
            return Unauthorized("Incorrect bearer token");
        }

        if (size < 1)
        {
            size = 1;
        }
        else if (size > 20 * Constants.OneMiB)
        {
            size = (int)(20 * Constants.OneMiB);
        }
        var bytes = new byte[size];
        bytes.AsSpan().Fill((byte)'X');
        return new FileStreamResult(bytes.ToMemoryStream(), "application/octet-stream"); // simulate payload response
    }

    //

    [HttpGet("cdn-ping/bad-cdn-path")]
    public ActionResult<string> CdnPingBadPath()
    {
        // SEB:NOTE this will never happen since CdnAuthPathHandler will reject the path before it reaches here
        return Ok("pong");
    }

    //
}
