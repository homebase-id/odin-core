using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Registry;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Plan;

/// <summary>
/// The identity's hosting plan (docs/hosting-plans.md), for the owner console and the owner's apps: what the plan
/// gives, and how much of its storage is used, so they can show "X of Y used" and explain a 507 before the user
/// hits one.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Plan)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2PlanController(TenantQuotaGuard quotaGuard) : OdinControllerBase
{
    [SwaggerOperation(Tags = [SwaggerInfo.Plan])]
    [HttpGet]
    [ProducesResponseType(typeof(IdentityPlan), 200)]
    public async Task<IdentityPlan> GetPlan()
    {
        WebOdinContext.Caller.AssertCallerIsOwner();
        return await quotaGuard.GetPlanAsync();
    }
}
