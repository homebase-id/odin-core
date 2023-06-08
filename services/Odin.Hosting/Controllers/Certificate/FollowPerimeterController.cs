using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Hosting.Authentication.Perimeter;

namespace Odin.Hosting.Controllers.Certificate
{
    /// <summary />
    [ApiController]
    [Route("api/perimeter/followers")]
    [Authorize(Policy = CertificatePerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PerimeterAuthConstants.PublicTransitAuthScheme)]
    public class FollowPerimeterController : ControllerBase
    {
        private readonly FollowerPerimeterService _followerPerimeterService;
        private readonly IPublicKeyService _rsaPublicKeyService;
        
        /// <summary />
        public FollowPerimeterController(IPublicKeyService rsaPublicKeyService, FollowerPerimeterService followerPerimeterService)
        {
            _rsaPublicKeyService = rsaPublicKeyService;
            _followerPerimeterService = followerPerimeterService;
        }

        /// <summary />
        [HttpPost("follow")]
        public async Task<IActionResult> ReceiveFollowRequest([FromBody] RsaEncryptedPayload payload)
        {
            var (isValidPublicKey, payloadBytes) = await _rsaPublicKeyService.DecryptPayloadUsingOfflineKey(payload);
            if (isValidPublicKey == false)
            {
                //TODO: extend with error code indicated a bad public key 
                return BadRequest("Invalid Public Key");
            }

            var request = OdinSystemSerializer.Deserialize<PerimterFollowRequest>(payloadBytes.ToStringFromUtf8Bytes());
            await _followerPerimeterService.AcceptFollower(request);

            return Ok();
        }
        
        /// <summary />
        [HttpPost("unfollow")]
        public async Task<IActionResult> ReceiveUnfollowRequest()
        {
            await _followerPerimeterService.AcceptUnfollowRequest();
            return Ok();
        }
    }
}