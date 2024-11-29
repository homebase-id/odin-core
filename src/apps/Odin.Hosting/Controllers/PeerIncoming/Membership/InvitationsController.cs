using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Core.Fluff;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.PeerIncoming.Membership
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
    public class InvitationsController(
        CircleNetworkRequestService circleNetworkRequestService) : OdinControllerBase
    {
        [HttpPost("connect")]
        public async Task<IActionResult> ReceiveConnectionRequest([FromBody] EccEncryptedPayload payload)
        {
            await circleNetworkRequestService.ReceiveConnectionRequestAsync(payload, HttpContext.RequestAborted, WebOdinContext);
            return Ok();
        }


        [HttpPost("establishconnection")]
        public async Task<IActionResult> EstablishConnection([FromBody] SharedSecretEncryptedPayload payload)
        {
            if (!HttpContext.Request.Headers.TryGetValue(OdinHeaderNames.EstablishConnectionAuthToken, out var authenticationToken64))
            {
                throw new OdinSecurityException("missing auth token");
            }
            await circleNetworkRequestService.EstablishConnection(payload, authenticationToken64, WebOdinContext);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}