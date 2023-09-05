using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Transit;
using Odin.Core.Services.Transit.ReceivingHost.Reactions;
using Odin.Core.Services.Transit.SendingHost;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.Peer
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.ReactionsV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class PeerPerimeterReactionContentController : OdinControllerBase
    {
        private readonly TransitReactionPerimeterService _reactionPerimeterService;

        public PeerPerimeterReactionContentController(TransitReactionPerimeterService reactionPerimeterService)
        {
            _reactionPerimeterService = reactionPerimeterService;
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddReactionContent(SharedSecretEncryptedTransitPayload payload)
        {
            await _reactionPerimeterService.AddReaction(payload);
            return NoContent();
        }

        [HttpPost("list")]
        public async Task<GetReactionsPerimeterResponse> GetAllReactions(SharedSecretEncryptedTransitPayload payload)
        {
            return await _reactionPerimeterService.GetReactions(payload);
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteReactionContent([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            await _reactionPerimeterService.DeleteReaction(payload);
            return NoContent();
        }

        [HttpPost("deleteall")]
        public async Task<IActionResult> DeleteAllReactionsOnFile([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            await _reactionPerimeterService.DeleteAllReactions(payload);
            return NoContent();
        }

        [HttpPost("summary")]
        public async Task<GetReactionCountsResponse> GetReactionCountsByFile([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            return await _reactionPerimeterService.GetReactionCountsByFile(payload);
        }

        [HttpPost("listbyidentity")]
        public async Task<List<string>> GetReactionsByIdentity([FromBody] SharedSecretEncryptedTransitPayload payload)
        {
            return await _reactionPerimeterService.GetReactionsByIdentityAndFile(payload);
        }
    }
}