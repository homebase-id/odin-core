using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Authentication.System;

namespace Odin.Hosting.Controllers.System
{
    /// <summary>
    /// Controller to enable kickoff of background tasks.  By running this over http, we keep the multi-tenant pattern working
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.PeerV1 + "/outbox/processor")]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    public class OutboxProcessorController(IPeerOutgoingTransferService peerOutgoingTransfer) : ControllerBase
    {
        [HttpPost("process")]
        public async Task<bool> ProcessOutbox()
        {
            //test
            await peerOutgoingTransfer.ProcessOutbox();
            return true;
        }
    }
}