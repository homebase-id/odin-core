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
    [ApiExplorerSettings(GroupName = "peer-v1")]
    public class PeerPerimeterReactionContentController(PeerIncomingReactionService incomingReactionService) : OdinControllerBase
    {
        [HttpPost("add")]
        public async Task<IActionResult> AddReactionContent(SharedSecretEncryptedTransitPayload payload)
        {
            
            await incomingReactionService.AddReaction(payload, WebOdinContext);
            return NoContent();
        }

        [HttpPost("list")]
        public async Task<GetReactionsPerimeterResponse> GetAllReactions(SharedSecretEncryptedTransitPayload payload)
        {
            
            return await incomingReactionService.GetReactions(payload, WebOdinContext);
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            
            await incomingReactionService.DeleteReaction(payload, WebOdinContext);
            return NoContent();
        }

        [HttpPost("deleteall")]
        public async Task<IActionResult> DeleteAllReactionsOnFile([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            
            await incomingReactionService.DeleteAllReactions(payload, WebOdinContext);
            return NoContent();
        }

        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            
            return await incomingReactionService.GetReactionCountsByFile(payload, WebOdinContext);
        }

        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            
            return await incomingReactionService.GetReactionsByIdentityAndFile(payload, WebOdinContext);
        }
    }
}