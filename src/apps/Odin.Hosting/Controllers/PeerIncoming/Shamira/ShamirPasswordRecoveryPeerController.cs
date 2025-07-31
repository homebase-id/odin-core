using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner.Shamira;
using Odin.Services.Peer;

namespace Odin.Hosting.Controllers.PeerIncoming.Shamira
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.PasswordRecoveryV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.PublicTransitAuthScheme)]
    public class ShamirPasswordRecoveryPeerController(
        ShamiraRecoveryService shamiraRecoveryService,
        ILogger<ShamirPasswordRecoveryPeerController> logger) : OdinControllerBase
    {
        [HttpPost("accept-shard")]
        public async Task<IActionResult> GetRsaKey(SendShardRequest request)
        {
            var caller = WebOdinContext.Caller.OdinId;
            //TODO: need to store the player shard request.DealerEncryptedShard
            return Ok();
        }
    }
}