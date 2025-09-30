#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Time;
using Odin.Hosting.Controllers.Base;
using Odin.Services;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Configuration.VersionUpgrade.Version0tov1;

namespace Odin.Hosting.Controllers.OwnerToken.DataConversion
{
    [ApiController]
    [Route(OwnerApiPathConstants.DataConversion)]
    [AuthorizeValidOwnerToken]
    public class OwnerDataConversionController(
        V0ToV1VersionMigrationService fixer,
        TenantConfigService configService,
        VersionUpgradeScheduler versionUpgradeScheduler) : OdinControllerBase
    {
        [HttpPost("autofix-connections")]
        public async Task<IActionResult> RunAutofix()
        {
            await fixer.AutoFixCircleGrantsAsync(WebOdinContext, HttpContext.RequestAborted);
            return Ok();
        }

        [HttpPost("force-version-number")]
        public async Task<IActionResult> ForceVersionReset([FromQuery] int version)
        {
            await configService.ForceVersionNumberAsync(version);
            return Ok();
        }

        [HttpPost("force-version-upgrade")]
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

        [HttpGet("data-version-info")]
        public async Task<ActionResult<VersionInfoResult>> GetVersionInfo()
        {
            var tenantVersionInfo = await configService.GetVersionInfoAsync();
            var (requiresUpgrade, _, failureInfo) = await versionUpgradeScheduler.RequiresUpgradeAsync();

            return new VersionInfoResult
            {
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
}

public class VersionInfoResult
{
    public bool RequiresUpgrade { get; init; }

    public int ServerDataVersionNumber { get; init; }

    /// <summary>
    /// The version number of the data structure for this tenant
    /// </summary>
    public int ActualDataVersionNumber { get; init; }

    public UnixTimeUtc LastUpgraded { get; init; }

    public int? FailedDataVersionNumber { get; init; }

    public UnixTimeUtc? LastAttempted { get; init; }

    public string? FailedBuildVersion { get; init; }
    public string? FailureCorrelationId { get; set; }
}