using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Onboarding;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Onboarding;

/// <summary>
/// The identity's onboarding info (#1908): who invited it, with which code, and whether Chat has acted
/// on it. For the owner console and the Chat app only; other apps get 403.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Onboarding)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2OnboardingController(OnboardingService onboardingService) : OdinControllerBase
{
    // GET /api/v2/onboarding — all fields null for an identity with no onboarding info
    [SwaggerOperation(Tags = [SwaggerInfo.Onboarding])]
    [HttpGet]
    [ProducesResponseType(typeof(OnboardingInfo), 200)]
    [ProducesResponseType(403)]
    public async Task<OnboardingInfo> Get()
    {
        return await onboardingService.GetAsync(WebOdinContext);
    }

    // POST /api/v2/onboarding/handled — Chat only; the first call sets handledAt, later calls change nothing
    [SwaggerOperation(Tags = [SwaggerInfo.Onboarding])]
    [HttpPost("handled")]
    [ProducesResponseType(typeof(OnboardingInfo), 200)]
    [ProducesResponseType(403)]
    public async Task<OnboardingInfo> MarkHandled()
    {
        return await onboardingService.MarkHandledAsync(WebOdinContext);
    }
}
