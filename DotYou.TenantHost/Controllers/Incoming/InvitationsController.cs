using DotYou.Kernel.Services.TrustNetwork;
using DotYou.Types.TrustNetwork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DotYou.TenantHost.Controllers.Incoming
{
    /// <summary>
    /// Controller which accepts various invitations.  This controller 
    /// must only add invitations and make no other changes.
    /// </summary>
    [ApiController]
    [Route("api/incoming/invitations")]
    public class InvitationsController : ControllerBase
    {
        private readonly ITrustNetworkService _trustNetwork;
        
        public InvitationsController(ITrustNetworkService trustNetwork)
        {
            _trustNetwork = trustNetwork;
        }

        [HttpPost("connect")]
        //[Authorize(Policy = PolicyNames.MustBeIdentified)]
        public IActionResult ReceiveConnectionRequest([FromBody] ConnectionRequest request)
        {
            _trustNetwork.ReceiveConnectionRequest(request);
            return Ok();
        }
    }
}
