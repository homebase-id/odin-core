﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Hosting.Authentication.TransitPerimeter;

namespace Youverse.Hosting.Controllers.TransitPerimeter
{
    /// <summary>
    /// Controller which accepts various invitations.  This controller 
    /// must only add invitations and make no other changes.
    /// </summary>
    [ApiController]
    [Route("api/perimeter/invitations")]
    [Authorize(Policy = TransitPerimeterPolicies.MustBeIdentifiedPolicyName, AuthenticationSchemes = TransitPerimeterAuthConstants.TransitAuthScheme)]
    public class InvitationsController : ControllerBase
    {
        private readonly ICircleNetworkRequestService _circleNetworkRequestService;

        public InvitationsController(ICircleNetworkRequestService circleNetworkRequestService)
        {
            _circleNetworkRequestService = circleNetworkRequestService;
        }

        [HttpPost("connect")]
        public async Task<IActionResult> ReceiveConnectionRequest([FromBody] ConnectionRequest request)
        {
            await _circleNetworkRequestService.ReceiveConnectionRequest(request);
            return new JsonResult(new NoResultResponse(true));
        }


        [HttpPost("establishconnection")]
        public async Task<IActionResult> EstablishConnection([FromBody] AcknowledgedConnectionRequest request)
        {
            await _circleNetworkRequestService.EstablishConnection(request);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}