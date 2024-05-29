using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.Connections
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidOwnerToken]
    public class OwnerConnectionsAutoFix(ConnectionAutoFixService fixer) : OdinControllerBase
    {
        [HttpPost("autofix")]
        public async Task<IActionResult> RunAutofix()
        {
            await fixer.AutoFix(WebOdinContext);
            return Ok();
        }
    }
}