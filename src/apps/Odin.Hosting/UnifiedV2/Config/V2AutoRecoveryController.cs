#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Configuration;
using Odin.Services.Registry;

namespace Odin.Hosting.UnifiedV2.Config;

/// <summary>
/// The first-run page asks whether this host can recover a password automatically, before any password exists. Open as
/// on V1, with the first-run token as the proof, and so apart from <see cref="V2ConfigurationController"/>, which is
/// owner only. It reads no caller, so a cookie left in the browser can't make it expect an encrypted query string.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Configuration)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2AutoRecoveryController(TenantConfigService tenantConfigService, IIdentityRegistry registry) : OdinControllerBase
{
    [HttpGet("can-use-auto-recovery")]
    public async Task<IActionResult> CanUseAutoRecovery([FromQuery(Name = "frt")] Guid firstRunToken)
    {
        await registry.AssetValidFirstRunToken(firstRunToken, WebOdinContext);
        tenantConfigService.AssertCanUseAutoPasswordRecovery(WebOdinContext);
        return Ok();
    }
}
