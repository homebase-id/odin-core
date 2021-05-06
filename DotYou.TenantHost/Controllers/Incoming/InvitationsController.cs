using DotYou.Kernel.Services.Circle;
using DotYou.Types;
using DotYou.Types.Circle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Identity;

namespace DotYou.TenantHost.Controllers.Incoming
{
    /// <summary>
    /// Controller which accepts various invitations.  This controller 
    /// must only add invitations and make no other changes.
    /// </summary>
    [ApiController]
    [Route("api/incoming/invitations")]
    [Authorize(Policy = DotYouPolicyNames.MustBeIdentified)]
    public class InvitationsController : ControllerBase
    {
        private readonly ICircleNetworkService _circleNetwork;

        public InvitationsController(ICircleNetworkService circleNetwork)
        {
            _circleNetwork = circleNetwork;
        }

        [HttpPost("connect")]
        public async Task<IActionResult> ReceiveConnectionRequest([FromBody] ConnectionRequest request)
        {
            request.SenderPublicKeyCertificate = User.FindFirstValue(DotYouClaimTypes.PublicKeyCertificateXml);
            await _circleNetwork.ReceiveConnectionRequest(request);
            return Ok();
        }

        
        [HttpPost("establishconnection")]
        public async Task<IActionResult> EstablishConnection([FromBody] EstablishConnectionRequest request)
        {
            await _circleNetwork.EstablishConnection(request);
            return Ok();
        }
    }
}
