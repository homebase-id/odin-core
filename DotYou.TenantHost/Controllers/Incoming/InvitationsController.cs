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
    public class InvitationsController : ControllerBase
    {
        private readonly ITrustNetworkService _trustNetwork;

        public InvitationsController(ITrustNetworkService trustNetwork)
        {
            _trustNetwork = trustNetwork;
        }

        [HttpPost("connect")]
        //[Authorize(Policy = PolicyNames.MustBeIdentified)]
        public async Task<IActionResult> ReceiveConnectionRequest([FromBody] ConnectionRequest request)
        {
            await _trustNetwork.ReceiveConnectionRequest(request);
            return Ok();
        }

        
        [HttpPost("establishconnection")]
        public async Task<IActionResult> EstablishConnection([FromBody] EstablishConnectionRequest request)
        {
            await _trustNetwork.EstablishConnection(request);
            return Ok();
        }
    }
}
