using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Dns.Health;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Security;

/// <summary>
/// DNS health for the owner console's Security tab: V2 of <c>OwnerDnsHealthController</c>, read-only.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Dns)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DnsHealthController(DnsHealthService dnsHealthService) : OdinControllerBase
{
    [HttpGet("status")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> GetStatus()
    {
        var domain = WebOdinContext.Tenant.AsciiDomain;
        var result = await dnsHealthService.GetDnsHealthAsync(domain, HttpContext.RequestAborted);
        return new JsonResult(result);
    }
}
