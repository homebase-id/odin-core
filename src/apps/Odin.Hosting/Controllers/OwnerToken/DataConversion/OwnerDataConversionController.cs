using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.DataConversion;

namespace Odin.Hosting.Controllers.OwnerToken.DataConversion
{
    [ApiController]
    [Route(OwnerApiPathConstants.DataConversion)]
    [AuthorizeValidOwnerToken]
    public class OwnerDataConversionController(DataConversionService fixer) : OdinControllerBase
    {
        [HttpPost("autofix-connections")]
        public async Task<IActionResult> RunAutofix()
        {
            await fixer.AutoFixCircleGrants(WebOdinContext);
            return Ok();
        }
        
        [HttpPost("ensure-verification-hash")]
        public async Task<IActionResult> EnsureVerificationHash()
        {
            await fixer.PrepareIntroductionsRelease(WebOdinContext);
            return Ok();
        }
        
    }
}