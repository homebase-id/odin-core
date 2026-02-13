using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Connections;

[ApiController]
[Route(UnifiedApiRouteConstants.Connections)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2ConnectionIntroductionsController(
    CircleNetworkIntroductionService introductionService
) : OdinControllerBase
{
    // GET /introductions
    [SwaggerOperation(Tags = [SwaggerInfo.Connections])]
    [HttpGet("introductions")]
    public async Task<IActionResult> GetIntroductions()
    {
        var list = await introductionService
            .GetReceivedIntroductionsAsync(WebOdinContext);

        return Ok(list);
    }

    // POST /introductions
    [SwaggerOperation(Tags = [SwaggerInfo.Connections])]
    [HttpPost("introductions")]
    public async Task<IActionResult> SendIntroductions(
        [FromBody] IntroductionGroup group)
    {
        OdinValidationUtils.AssertNotNull(group, nameof(group));
        OdinValidationUtils.AssertValidRecipientList(group.Recipients);

        var result = await introductionService
            .SendIntroductions(group, WebOdinContext);

        return Ok(result);
    }

    // POST /introductions/process
    [SwaggerOperation(Tags = [SwaggerInfo.Connections])]
    [HttpPost("introductions/process")]
    public async Task<IActionResult> ProcessIncomingIntroductions()
    {
        await introductionService
            .SendOutstandingConnectionRequestsAsync(
                WebOdinContext,
                HttpContext.RequestAborted);

        return NoContent();
    }

    // POST /introductions/auto-accept
    [SwaggerOperation(Tags = [SwaggerInfo.Connections])]
    [HttpPost("introductions/auto-accept")]
    public async Task<IActionResult> AutoAcceptEligibleIntroductions()
    {
        await introductionService
            .ForceAutoAcceptEligibleConnectionRequestsAsync(
                WebOdinContext,
                HttpContext.RequestAborted);

        return NoContent();
    }

    // DELETE /introductions
    [SwaggerOperation(Tags = [SwaggerInfo.Connections])]
    [HttpDelete("introductions")]
    public async Task<IActionResult> DeleteAllIntroductions()
    {
        await introductionService
            .DeleteIntroductionsAsync(WebOdinContext);

        return NoContent();
    }
}