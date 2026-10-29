using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Peer;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Controllers.PeerIncoming.Shamira
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.PasswordRecoveryV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCapiAuthScheme)]
    [ApiExplorerSettings(GroupName = "peer-v1")]
    public class ShamirPasswordRecoveryPeerController(
        ShamirReadinessCheckerService readinessCheckerService,
        ShamirRecoveryService recoveryService) : OdinControllerBase
    {
        [HttpPost("verify-readiness")]
        public async Task<IActionResult> VerifyReadiness()
        {
            var result = await readinessCheckerService.VerifyReadiness(WebOdinContext);
            return Ok(result);
        }
        
        [HttpPost("verify-shard")]
        public async Task<IActionResult> VerifyShard(VerifyShardRequest request)
        {
            var result = await readinessCheckerService.VerifyDealerShard(request.ShardId, WebOdinContext);
            return Ok(result);
        }

        [HttpPost("request-shard")]
        public async Task<RetrieveShardResult> RequestShard(RetrieveShardRequest request)
        {
            var result = await recoveryService.HandleReleaseShardRequest(request, WebOdinContext);
            return result;
        }

        /// <summary>
        /// As an automatic holder: the dealer passes on the code from the link this holder emailed the
        /// owner, and gets the shard sealed to the recovering browser's key.
        /// </summary>
        [HttpPost("release-automatic-shard")]
        public async Task<RetrieveShardResult> ReleaseAutomaticShard(ReleaseAutomaticShardRequest request)
        {
            return await recoveryService.HandleReleaseAutomaticShardAsync(request, WebOdinContext);
        }

        [HttpPost("accept-player-shard")]
        public async Task<IActionResult> AcceptPlayerShard([FromBody] RetrieveShardResult result)
        {
            await recoveryService.HandleAcceptRecoveryShard(result, WebOdinContext);
            return Ok();
        }
    }
}