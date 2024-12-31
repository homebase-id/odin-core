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
using Odin.Services.Base;

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
        public FollowPerimeterController(PublicPrivateKeyService publicPrivatePublicKeyService,
            FollowerPerimeterService followerPerimeterService)
        {
            _publicPrivatePublicKeyService = publicPrivatePublicKeyService;
            _followerPerimeterService = followerPerimeterService;
        }

        /// <summary />
        [HttpPost("follow")]
        public async Task<IActionResult> ReceiveFollowRequest([FromBody] EccEncryptedPayload payload)
        {
            OdinValidationUtils.AssertNotNull(payload, nameof(payload));
            
            var payloadBytes = await _publicPrivatePublicKeyService.EccDecryptPayload(payload, WebOdinContext);

            var request = OdinSystemSerializer.Deserialize<PerimeterFollowRequest>(payloadBytes.ToStringFromUtf8Bytes());
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertIsValidOdinId(request.OdinId, out _);

            await _followerPerimeterService.AcceptFollowerAsync(request, WebOdinContext);

            return Ok();
        }

        /// <summary />
        [HttpPost("unfollow")]
        public async Task<IActionResult> ReceiveUnfollowRequest()
        {
            await _followerPerimeterService.AcceptUnfollowRequestAsync(WebOdinContext);
            return Ok();
        }
    }
}