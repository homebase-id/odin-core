﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        CircleNetworkRequestService circleNetworkRequestService,
        CircleNetworkIntroductionService introductionService,
        TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        [HttpPost("connect")]
        public async Task<IActionResult> ReceiveConnectionRequest([FromBody] RsaEncryptedPayload payload)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await circleNetworkRequestService.ReceiveConnectionRequest(payload, WebOdinContext, cn);
            return new JsonResult(new NoResultResponse(true));
        }


        [HttpPost("establishconnection")]
        public async Task<IActionResult> EstablishConnection([FromBody] SharedSecretEncryptedPayload payload, string authenticationToken64)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await circleNetworkRequestService.EstablishConnection(payload, authenticationToken64, WebOdinContext, cn);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpPost("request-introductions")]
        public async Task<IActionResult> ReceiveIntroductionRequest([FromBody] SharedSecretEncryptedPayload payload)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await introductionService.ReceiveIntroductionRequest(payload, WebOdinContext, cn);
            return Ok();
        }
        
        
        [HttpPost("make-introduction")]
        public async Task<IActionResult> ReceiveIntroduction([FromBody] SharedSecretEncryptedPayload payload)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await introductionService.ReceiveIntroduction(payload, WebOdinContext, cn);
            return Ok();
        }
    }
}