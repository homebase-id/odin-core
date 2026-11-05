using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.DataConversion;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Security;

/// <summary>
/// The identity's data upgrade: V2 of <c>OwnerDataConversionController</c>, same services, owner console only.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.DataConversion)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DataConversionController(
    TenantConfigService configService,
    VersionUpgradeScheduler versionUpgradeScheduler) : OdinControllerBase
{
    [HttpPost("force-version-number")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> ForceVersionReset([FromQuery] int version)
    {
        var result = await configService.ForceVersionNumberAsync(version);
        return Ok(result);
    }

    [HttpPost("force-version-upgrade")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> ForceVersionUpgrade()
    {
        var value = Request.Cookies[OwnerAuthConstants.CookieName];
        if (ClientAuthenticationToken.TryParse(value, out var result))
        {
            await versionUpgradeScheduler.EnsureScheduledAsync(result, WebOdinContext, force: true);
            return Ok();
        }

        return BadRequest();
    }

    /// <summary>
    /// Exempt from <c>VersionUpgradeMiddleware</c>'s 503, which goes by this endpoint's name in the path, so keep the name.
    /// </summary>
    [HttpGet(OwnerDataConversionController.VersionInfoEndpoint)]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<ActionResult<VersionInfoResult>> GetVersionInfo()
    {
        var tenantVersionInfo = await configService.GetVersionInfoAsync();
        var (requiresUpgrade, tenantVersion, failureInfo) = await versionUpgradeScheduler.RequiresUpgradeAsync();

        return new VersionInfoResult
        {
            UpgradeState = versionUpgradeScheduler.GetUpgradeState(requiresUpgrade, tenantVersion, failureInfo),
            RequiresUpgrade = requiresUpgrade,
            ServerDataVersionNumber = Version.DataVersionNumber,
            ActualDataVersionNumber = tenantVersionInfo.DataVersionNumber,
            LastUpgraded = tenantVersionInfo.LastUpgraded,
            FailedDataVersionNumber = failureInfo?.FailedDataVersionNumber,
            LastAttempted = failureInfo?.LastAttempted,
            FailedBuildVersion = failureInfo?.BuildVersion,
            FailureCorrelationId = failureInfo?.CorrelationId
        };
    }
}
