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
            await fixer.AutoFixCircleGrants(WebOdinContext);
            return Ok();
        }

        [HttpPost("force-version-number")]
        public Task<IActionResult> ForceVersionReset([FromQuery] int version)
        {
            configService.ForceVersionNumber(version);
            return Task.FromResult(Ok() as IActionResult);
        }
    }
}