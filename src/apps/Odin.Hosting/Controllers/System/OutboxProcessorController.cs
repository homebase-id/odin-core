using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Time;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Authentication.System;
using Odin.Services.Configuration;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Controllers.System
{
    /// <summary>
    /// Controller to enable kickoff of background tasks. 
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.PeerV1 + "/outbox/processor")]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    public class OutboxProcessorController(
        OdinConfiguration config,
        PeerOutboxProcessor outboxProcessor,
        PeerOutbox outbox,
        PeerInbox inbox) : ControllerBase
    {
        [HttpPost("initiate")]
        public async Task<IActionResult> InitiateOutboxProcessing()
        {
            await outboxProcessor.ProcessOutbox();
            return Ok();
        }

        [HttpPost("reconcile")]
        public async Task<IActionResult> ReconcileInboxOutbox()
        {
            var ageSeconds = config.Host.InboxOutboxRecoveryAgeSeconds;
            var time = UnixTimeUtc.FromDateTime(DateTime.Now.Subtract(TimeSpan.FromSeconds(ageSeconds)));

            await outbox.RecoverDead(time);
            await inbox.RecoverDead(time);
            return Ok();
        }
    }
}