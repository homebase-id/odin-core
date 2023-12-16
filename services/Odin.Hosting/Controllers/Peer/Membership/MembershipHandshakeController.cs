using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Fluff;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.Connections.Requests;
using Odin.Core.Services.Peer;
using Odin.Hosting.Authentication.Peer;

namespace Odin.Hosting.Controllers.Peer.Membership
{
    /// <summary>
    /// Handles the final processes of establishing a connection
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.InvitationsV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class MembershipHandshakeController : ControllerBase
    {
        private readonly CircleNetworkRequestService _circleNetworkRequestService;

        public MembershipHandshakeController(CircleNetworkRequestService circleNetworkRequestService)
        {
            _circleNetworkRequestService = circleNetworkRequestService;
        }
        
        [HttpPost("finalizeconnection")]
        public async Task<IActionResult> FinalizeConnection([FromBody] SharedSecretEncryptedPayload payload)
        {
            await _circleNetworkRequestService.FinalizeConnection(payload);
            return Ok();
        }
    }
}