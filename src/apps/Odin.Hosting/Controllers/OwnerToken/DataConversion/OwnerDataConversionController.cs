using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade.Version0tov1;

namespace Odin.Hosting.Controllers.OwnerToken.DataConversion
{
    [ApiController]
    [Route(OwnerApiPathConstants.DataConversion)]
    [AuthorizeValidOwnerToken]
    public class OwnerDataConversionController(V0ToV1VersionMigrationService fixer, TenantConfigService configService) : OdinControllerBase
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
    }
}