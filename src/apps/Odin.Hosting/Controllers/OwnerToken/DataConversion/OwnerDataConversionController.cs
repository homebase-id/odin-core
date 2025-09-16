using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
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
    public class OwnerDataConversionController(V0ToV1VersionMigrationService fixer, TenantConfigService configService, 
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
                await versionUpgradeScheduler.EnsureScheduledAsync(result, WebOdinContext);    
                return Ok();
            }

            return BadRequest();
        }
    }
    
}