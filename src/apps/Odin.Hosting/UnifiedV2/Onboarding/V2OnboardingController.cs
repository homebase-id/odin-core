using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Registry;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Onboarding;

/// <summary>
/// Who invited this identity (#1908), read from its registration so the owner console's setup wizard can
/// offer to connect with them. Owner console only.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Onboarding)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2OnboardingController(IIdentityRegistry registry) : OdinControllerBase
{
    /// <param name="InvitedBy">The invitation code's owner; null when the code had none, or there was no code</param>
    public record InvitedByResponse(string InvitedBy);

    // GET /api/v2/onboarding/invited-by
    [SwaggerOperation(Tags = [SwaggerInfo.Onboarding])]
    [HttpGet("invited-by")]
    [ProducesResponseType(typeof(InvitedByResponse), 200)]
    public async Task<InvitedByResponse> GetInvitedBy()
    {
        WebOdinContext.Caller.AssertHasMasterKey();

        var registration = await registry.GetAsync(WebOdinContext.Tenant.DomainName);
        return new InvitedByResponse(registration?.Invitation?.InvitedBy);
    }
}
