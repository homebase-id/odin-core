using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer;
using Odin.Services.Peer.Incoming.Drive.Reactions;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.PeerIncoming.Drive
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.ReactionsV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class PeerPerimeterReactionContentController(PeerIncomingReactionService incomingReactionService, TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        [HttpPost("add")]
        public async Task<IActionResult> AddReactionContent(SharedSecretEncryptedTransitPayload payload)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await incomingReactionService.AddReaction(payload, WebOdinContext, cn);
            return NoContent();
        }

        [HttpPost("list")]
        public async Task<GetReactionsPerimeterResponse> GetAllReactions(SharedSecretEncryptedTransitPayload payload)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            return await incomingReactionService.GetReactions(payload, WebOdinContext, cn);
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await incomingReactionService.DeleteReaction(payload, WebOdinContext, cn);
            return NoContent();
        }

        [HttpPost("deleteall")]
        public async Task<IActionResult> DeleteAllReactionsOnFile([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await incomingReactionService.DeleteAllReactions(payload, WebOdinContext, cn);
            return NoContent();
        }

        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            return await incomingReactionService.GetReactionCountsByFile(payload, WebOdinContext, cn);
        }

        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            return await incomingReactionService.GetReactionsByIdentityAndFile(payload, WebOdinContext, cn);
        }
    }
}