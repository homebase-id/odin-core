﻿using DotYou.Kernel.Services.TrustNetwork;
using DotYou.Types;
using DotYou.Types.TrustNetwork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace DotYou.TenantHost.Controllers.Incoming
{
    /// <summary>
    /// Controller which accepts various invitations.  This controller 
    /// must only add invitations and make no other changes.
    /// </summary>
    [ApiController]
    [Route("api/incoming/invitations")]
    //[Authorize(Policy = PolicyNames.MustBeIdentified)]
    public class InvitationsController : ControllerBase
    {
        private readonly ICircleNetworkService _circleNetwork;

        public InvitationsController(ICircleNetworkService cirlceNetwork)
        {
            _circleNetwork = cirlceNetwork;
        }

        [HttpPost("connect")]
        public async Task<IActionResult> ReceiveConnectionRequest([FromBody] ConnectionRequest request)
        {
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
