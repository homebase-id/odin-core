using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Registry.Provisioning;

namespace Youverse.Hosting.Controllers.Owner.Provisioning
{
    [ApiController]
    [Route(OwnerApiPathConstants.ProvisioningV1)]
    [AuthorizeOwnerConsole]
    public class ProvisioningController : Controller
    {
        private readonly IIdentityProvisioner _identityProvisioner;

        public ProvisioningController(IIdentityProvisioner identityProvisioner)
        {
            _identityProvisioner = identityProvisioner;
        }

        //TODO: will need to send a callbackId or increase the timeout
        [HttpPost("systemapps")]
        public async Task<IActionResult> EnsureSystemApps()
        {
            await _identityProvisioner.EnsureSystemApps();
            return new JsonResult(new NoResultResponse(true));
        }
    }
}