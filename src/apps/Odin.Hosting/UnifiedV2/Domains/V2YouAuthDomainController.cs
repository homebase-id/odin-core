using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Util;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Membership.YouAuth;

namespace Odin.Hosting.UnifiedV2.Domains;

/// <summary>
/// The V2 twin of V1's <c>youauthdomain</c> (<see cref="YouAuthDomainRegistrationController"/>), with its request and
/// response shapes. Owner console only, as V1 is.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.YouAuthDomains)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2YouAuthDomainController(YouAuthDomainRegistrationService registrationService) : OdinControllerBase
{
    [HttpGet("list")]
    public async Task<List<RedactedYouAuthDomainRegistration>> GetRegisteredDomains()
    {
        return await registrationService.GetRegisteredDomainsAsync(WebOdinContext);
    }

    [HttpPost("domain")]
    public async Task<IActionResult> GetRegisteredDomain([FromBody] GetYouAuthDomainRequest request)
    {
        var reg = await registrationService.GetRegistration(new AsciiDomainName(request.Domain), WebOdinContext);
        return reg == null ? NotFound() : new JsonResult(reg);
    }

    [HttpPost("circles/add")]
    public async Task<bool> GrantCircle([FromBody] GrantYouAuthDomainCircleRequest request)
    {
        await registrationService.GrantCircleAsync(request.CircleId, new AsciiDomainName(request.Domain), WebOdinContext);
        return true;
    }

    [HttpPost("circles/revoke")]
    public async Task<bool> RevokeCircle([FromBody] RevokeYouAuthDomainCircleRequest request)
    {
        await registrationService.RevokeCircleAccess(request.CircleId, new AsciiDomainName(request.Domain), WebOdinContext);
        return true;
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> RevokeDomain([FromBody] GetYouAuthDomainRequest request)
    {
        await registrationService.RevokeDomainAsync(new AsciiDomainName(request.Domain), WebOdinContext);
        return Ok();
    }

    [HttpPost("allow")]
    public async Task<IActionResult> AllowDomain([FromBody] GetYouAuthDomainRequest request)
    {
        await registrationService.RemoveDomainRevocationAsync(new AsciiDomainName(request.Domain), WebOdinContext);
        return Ok();
    }

    [HttpPost("delete-domain")]
    public async Task<IActionResult> DeleteDomain([FromBody] GetYouAuthDomainRequest request)
    {
        await registrationService.DeleteDomainRegistrationAsync(new AsciiDomainName(request.Domain), WebOdinContext);
        return Ok();
    }

    [HttpGet("clients")]
    public async Task<List<RedactedYouAuthDomainClient>> GetRegisteredClients(string domain)
    {
        return await registrationService.GetRegisteredClientsAsync(new AsciiDomainName(domain), WebOdinContext);
    }

    [HttpPost("delete-client")]
    public async Task DeleteClient([FromBody] GetYouAuthDomainClientRequest request)
    {
        await registrationService.DeleteClientAsync(request.AccessRegistrationId, WebOdinContext);
    }
}
