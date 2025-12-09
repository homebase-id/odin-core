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
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
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

        [HttpPost("accept-player-shard")]
        public async Task<IActionResult> AcceptPlayerShard([FromBody] RetrieveShardResult result)
        {
            await recoveryService.HandleAcceptRecoveryShard(result, WebOdinContext);
            return Ok();
        }
    }
}