using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Peer;
using Odin.Services.ShamiraPasswordRecovery;

namespace Odin.Hosting.Controllers.PeerIncoming.Shamira
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.PasswordRecoveryV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.PublicTransitAuthScheme)]
    public class ShamirPasswordRecoveryPeerController(
        ShamiraVerificationService verificationService) : OdinControllerBase
    {
        [HttpPost("verify-shard")]
        public async Task<IActionResult> VerifyShard(VerifyShardRequest request)
        {
            var result = await verificationService.VerifyShard(request.ShardId, WebOdinContext);
            return Ok(result);
        }
    }
}