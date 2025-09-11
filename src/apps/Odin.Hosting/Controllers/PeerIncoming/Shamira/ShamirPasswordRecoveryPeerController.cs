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
    public class ShamirPasswordRecoveryPeerController(
        ShamirConfigurationService configurationService,
        ShamirRecoveryService recoveryService) : OdinControllerBase
    {
        [HttpPost("verify-shard")]
        public async Task<IActionResult> VerifyShard(VerifyShardRequest request)
        {
            var result = await configurationService.VerifyDealerShard(request.ShardId, request.RecoveryEmailHash, WebOdinContext);
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