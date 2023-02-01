using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Hosting.Authentication.Perimeter;

namespace Youverse.Hosting.Controllers.Certificate
{
    /// <summary />
    [ApiController]
    [Route("api/perimeter/followers")]
    [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = PerimeterAuthConstants.PublicTransitAuthScheme)]
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

            var request = DotYouSystemSerializer.Deserialize<PerimterFollowRequest>(payloadBytes.ToStringFromUtf8Bytes());
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