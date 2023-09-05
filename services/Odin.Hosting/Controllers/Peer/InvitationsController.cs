using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Fluff;
using Odin.Core.Services.Base;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Membership.Connections.Requests;
using Odin.Core.Services.Peer;
using Odin.Hosting.Authentication.Peer;

namespace Odin.Hosting.Controllers.Peer
{
    /// <summary>
    /// Controller which accepts various invitations.  This controller 
    /// must only add invitations and make no other changes.
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.InvitationsV1)]
    //so here i could change the transit to have two policies - one that requires an app and one that is an certificate only
    //how do you know it is the owner console tho?
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.PublicTransitAuthScheme)]
    public class InvitationsController : ControllerBase
    {
        private readonly CircleNetworkRequestService _circleNetworkRequestService;

        public InvitationsController(CircleNetworkRequestService circleNetworkRequestService)
        {
            _circleNetworkRequestService = circleNetworkRequestService;
        }

        [HttpPost("connect")]
        public async Task<IActionResult> ReceiveConnectionRequest([FromBody] RsaEncryptedPayload payload)
        {
            await _circleNetworkRequestService.ReceiveConnectionRequest(payload);
            return new JsonResult(new NoResultResponse(true));
        }


        [HttpPost("establishconnection")]
        public async Task<IActionResult> EstablishConnection([FromBody] SharedSecretEncryptedPayload payload, string authenticationToken64)
        {
            await _circleNetworkRequestService.EstablishConnection(payload, authenticationToken64);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}