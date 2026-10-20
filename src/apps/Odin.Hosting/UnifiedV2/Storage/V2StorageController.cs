using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Registry;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Storage;

/// <summary>
/// The identity's disk usage against its plan's quota (docs/hosting-plans.md), for the owner console and the
/// owner's apps, so they can show "X of Y used" and explain a 507 before the user hits one.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Storage)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2StorageController(TenantQuotaGuard quotaGuard) : OdinControllerBase
{
    [SwaggerOperation(Tags = [SwaggerInfo.Storage])]
    [HttpGet("usage")]
    [ProducesResponseType(typeof(StorageUsage), 200)]
    public async Task<StorageUsage> GetUsage()
    {
        WebOdinContext.Caller.AssertCallerIsOwner();
        return await quotaGuard.GetUsageAsync();
    }
}
