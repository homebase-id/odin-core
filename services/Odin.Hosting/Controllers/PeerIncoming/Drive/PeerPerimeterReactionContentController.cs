using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Incoming.Drive.Reactions;
using Odin.Core.Services.Peer.Incoming.Reactions;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.PeerIncoming.Drive
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.ReactionsV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class PeerPerimeterReactionContentController(PeerReactionService reactionService) : OdinControllerBase
    {
        [HttpPost("add")]
        public async Task<IActionResult> AddReactionContent(SharedSecretEncryptedTransitPayload payload)
        {
            await reactionService.AddReaction(payload);
            return NoContent();
        }

        [HttpPost("list")]
        public async Task<GetReactionsPerimeterResponse> GetAllReactions(SharedSecretEncryptedTransitPayload payload)
        {
            return await reactionService.GetReactions(payload);
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            await reactionService.DeleteReaction(payload);
            return NoContent();
        }

        [HttpPost("deleteall")]
        public async Task<IActionResult> DeleteAllReactionsOnFile([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            await reactionService.DeleteAllReactions(payload);
            return NoContent();
        }

        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            return await reactionService.GetReactionCountsByFile(payload);
        }

        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            return await reactionService.GetReactionsByIdentityAndFile(payload);
        }
    }
}