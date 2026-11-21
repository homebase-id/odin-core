#nullable enable
using Microsoft.AspNetCore.Mvc;
using Odin.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Ping;

/// <summary>
/// The server's version, as V1's <c>/api/v1/version</c> answers it. A plain link in the apps opens this, so it reads no
/// caller: an owner's cookie would otherwise get the answer encrypted with a shared secret the browser can't use.
/// </summary>
[Route(UnifiedApiRouteConstants.Version)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2VersionController : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Tags = [SwaggerInfo.Health])]
    public ActionResult<string> GetVersion() => Version.VersionText;
}
