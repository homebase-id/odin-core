using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Authorization.Capi;

namespace Odin.Hosting.UnifiedV2.Capi;

#nullable enable

[ApiController]
[Route(UnifiedApiRouteConstants.Capi)]
[UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
[ApiExplorerSettings(GroupName = "v2")]
public class CapiValidationController(ICapiCallbackSession capiCallbackSession) : ControllerBase
{
    [HttpGet("validate/{remoteDomainAndSessionId}")]
    public async Task<ActionResult<string>> Validate(string remoteDomainAndSessionId)
    {
        var capiRemoteDomainAndSessionId = remoteDomainAndSessionId.Split('~');
        if (capiRemoteDomainAndSessionId.Length != 2)
        {
            return NotFound("Invalid or missing session param");
        }

        var remoteDomain = capiRemoteDomainAndSessionId[0];
        if (string.IsNullOrWhiteSpace(remoteDomain))
        {
            return NotFound("Invalid or missing remote domain in session param");
        }

        var sessionId = capiRemoteDomainAndSessionId[1];
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return NotFound("Invalid or missing sessionId in session param");
        }

        var isValidSession = await capiCallbackSession.ValidateSessionAsync(remoteDomain, sessionId);

        if (!isValidSession)
        {
            return NotFound("Session not found");
        }

        return Ok();
    }
}
