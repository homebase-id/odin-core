using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Circle;
using DotYou.Types;
using DotYou.Types.Circle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;

namespace DotYou.DigitalIdentityHost.Controllers.Perimeter
{
    /// <summary>
    /// Controller which accepts various invitations.  This controller 
    /// must only add invitations and make no other changes.
    /// </summary>
    [ApiController]
    [Route("api/perimeter/invitations")]
    [Authorize(Policy = DotYouPolicyNames.MustBeIdentified)]
    public class InvitationsController : ControllerBase
    {
        private readonly ICircleNetworkRequestService _circleNetwork;

        public InvitationsController(ICircleNetworkRequestService circleNetwork)
        {
            _circleNetwork = circleNetwork;
        }

        [HttpPost("connect")]
        public async Task<IActionResult> ReceiveConnectionRequest([FromBody] ConnectionRequest request)
        {
            await _circleNetwork.ReceiveConnectionRequest(request);
            return new JsonResult(new NoResultResponse(true));
        }


        [HttpPost("establishconnection")]
        public async Task<IActionResult> EstablishConnection([FromBody] AcknowledgedConnectionRequest request)
        {
            await _circleNetwork.EstablishConnection(request);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}