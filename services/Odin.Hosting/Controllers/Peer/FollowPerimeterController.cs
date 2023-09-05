using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Transit;
using Odin.Hosting.Authentication.Peer;

namespace Odin.Hosting.Controllers.Peer
{
    /// <summary />
    [ApiController]
    [Route(PeerApiPathConstants.FollowersV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.PublicTransitAuthScheme)]
    public class FollowPerimeterController : ControllerBase
    {
        private readonly FollowerPerimeterService _followerPerimeterService;
        private readonly PublicPrivateKeyService _publicPrivatePublicKeyService;

        /// <summary />
        public FollowPerimeterController(PublicPrivateKeyService publicPrivatePublicKeyService, FollowerPerimeterService followerPerimeterService)
        {
            _publicPrivatePublicKeyService = publicPrivatePublicKeyService;
            _followerPerimeterService = followerPerimeterService;
        }

        /// <summary />
        [HttpPost("follow")]
        public async Task<IActionResult> ReceiveFollowRequest([FromBody] RsaEncryptedPayload payload)
        {
            var (isValidPublicKey, payloadBytes) = await _publicPrivatePublicKeyService.RsaDecryptPayload(RsaKeyType.OfflineKey, payload);
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