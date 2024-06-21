using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Time;
using Odin.Services.Authentication.Owner;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

// SEB:REVIEW this class should be removed. Outbox processing should be a BackgroundService

namespace Odin.Hosting.Controllers.System
{
    /// <summary>
    /// Controller to enable kickoff of background tasks.  By running this over http, we keep the multi-tenant pattern working
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.PeerV1 + "/outbox/processor")]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    public class OutboxProcessorController(
        OdinConfiguration config,
        PeerOutbox outbox,
        PeerOutboxProcessor outboxProcessor,
        PeerOutboxProcessorAsync outboxProcessorAsync,
        TenantSystemStorage tenantSystemStorage,
        TransitInboxBoxStorage inbox) : OdinControllerBase
    {
        [HttpPost("process")]
        public async Task<bool> ProcessOutbox()
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await outboxProcessor.StartOutboxProcessing(WebOdinContext, cn);
            return true;
        }

        [HttpPost("process-async")]
        public async Task<bool> ProcessOutboxAsync()
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await outboxProcessorAsync.StartOutboxProcessingAsync(WebOdinContext, cn);
            return true;
        }
        
        
        [HttpPost("reconcile")]
        public async Task<IActionResult> ReconcileInboxOutbox()
        {
            var ageSeconds = config.Host.InboxOutboxRecoveryAgeSeconds;
            var time = UnixTimeUtc.FromDateTime(DateTime.Now.Subtract(TimeSpan.FromSeconds(ageSeconds)));

            using var cn = tenantSystemStorage.CreateConnection();
            await outbox.RecoverDead(time, cn);
            await inbox.RecoverDead(time, cn);
            return Ok();
        }
    }
}