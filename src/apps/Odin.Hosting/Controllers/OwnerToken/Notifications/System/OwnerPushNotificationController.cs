#nullable enable

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Controllers.OwnerToken.Notifications.System
{
    [ApiController]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    [Route(OwnerApiPathConstants.PushNotificationsV1)]
    public class OwnerSystemPushNotificationController(
        PeerOutboxProcessor outboxProcessor,
        TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        [HttpPost("process")]
        public async Task<IActionResult> ProcessBatch()
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await outboxProcessor.StartOutboxProcessing(WebOdinContext, cn);
            return Ok();
        }
    }
}