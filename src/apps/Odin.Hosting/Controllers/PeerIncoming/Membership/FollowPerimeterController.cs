using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Peer;
using Odin.Services.Util;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.PeerIncoming.Membership
{
    /// <summary />
    [ApiController]
    [Route(PeerApiPathConstants.FollowersV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.PublicTransitAuthScheme)]
    public class FollowPerimeterController : OdinControllerBase
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
            OdinValidationUtils.AssertNotNull(payload, nameof(payload));
            OdinValidationUtils.AssertIsTrue(payload!.IsValid(), "Rsa Encrypted Payload is invalid");
            
            var (isValidPublicKey, payloadBytes) = await _publicPrivatePublicKeyService.RsaDecryptPayload(RsaKeyType.OfflineKey, payload);
            if (isValidPublicKey == false)
            {
                //TODO: extend with error code indicated a bad public key 
                return BadRequest("Invalid Public Key");
            }

            var request = OdinSystemSerializer.Deserialize<PerimeterFollowRequest>(payloadBytes.ToStringFromUtf8Bytes());
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertIsValidOdinId(request.OdinId, out _);
            
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