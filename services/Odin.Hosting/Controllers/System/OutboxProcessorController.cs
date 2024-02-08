﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Controllers.OwnerToken;

namespace Odin.Hosting.Controllers.System
{
    /// <summary>
    /// Controller to enable kickoff of background tasks.  By running this over http, we keep the multi-tenant pattern working
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.TransitV1 + "/outbox/processor")]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    public class OutboxProcessorController(IPeerTransferService peerTransfer) : ControllerBase
    {
        [HttpPost("process")]
        public async Task<bool> ProcessOutbox(int batchSize)
        {
            await peerTransfer.ProcessOutbox();
            return true;
        }
    }
}