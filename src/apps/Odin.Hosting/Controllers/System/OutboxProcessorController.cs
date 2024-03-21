using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Authentication.System;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Controllers.System
{
    /// <summary>
    /// Controller to enable kickoff of background tasks.  By running this over http, we keep the multi-tenant pattern working
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.PeerV1 + "/outbox/processor")]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    public class OutboxProcessorController(IPeerOutgoingTransferService peerOutgoingTransfer, IPeerOutbox outbox, TransitInboxBoxStorage inbox) : ControllerBase
    {
        [HttpPost("process")]
        public async Task<bool> ProcessOutbox()
        {
            await peerOutgoingTransfer.ProcessOutbox();
            return true;
        }
        
        [HttpPost("reconcile")]
        public async Task<IActionResult> ReconcileInboxOutbox()
        {
            await outbox.RecoverDead();
            await inbox.RecoverDead();
            return Ok();
        }
    }
}