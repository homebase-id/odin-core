using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Authorization.Capi;

namespace Odin.Hosting.UnifiedV2.Capi;

#nullable enable

[ApiController]
[Route(UnifiedApiRouteConstants.Capi)]
[UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
[ApiExplorerSettings(GroupName = "v2")]
public class CapiValidationController(ILogger<CapiValidationController> logger, ICapiCallbackSession capiCallbackSession) : ControllerBase
{
    [HttpGet("validate/{remoteDomainAndSessionId}")]
    public async Task<ActionResult<string>> Validate(string remoteDomainAndSessionId)
    {
        var capiRemoteDomainAndSessionId = remoteDomainAndSessionId.Split('~');
        if (capiRemoteDomainAndSessionId.Length != 2)
        {
            logger.LogWarning("CAPICAPICAPI CapiValidationController Invalid or missing session param {session}", remoteDomainAndSessionId); // SEB:TODO delete me
            return NotFound("Invalid or missing session param");
        }

        var remoteDomain = capiRemoteDomainAndSessionId[0];
        if (string.IsNullOrWhiteSpace(remoteDomain))
        {
            logger.LogWarning("CAPICAPICAPI CapiValidationController Invalid or missing remote domain in session param {session}", remoteDomainAndSessionId); // SEB:TODO delete me
            return NotFound("Invalid or missing remote domain in session param");
        }

        var sessionId = capiRemoteDomainAndSessionId[1];
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            logger.LogWarning("CAPICAPICAPI CapiValidationController Invalid or missing sessionId in session param {session}", remoteDomainAndSessionId); // SEB:TODO delete me
            return NotFound("Invalid or missing sessionId in session param");
        }

        var isValidSession = await capiCallbackSession.ValidateSessionAsync(remoteDomain, sessionId);

        if (!isValidSession)
        {
            logger.LogDebug("CAPICAPICAPI CapiValidationController Session not found {session}", remoteDomainAndSessionId); // SEB:TODO delete me
            return NotFound("Session not found");
        }

        logger.LogDebug("CAPICAPICAPI CapiValidationController Session found {session}", remoteDomainAndSessionId); // SEB:TODO delete me
        return Ok();
    }
}
