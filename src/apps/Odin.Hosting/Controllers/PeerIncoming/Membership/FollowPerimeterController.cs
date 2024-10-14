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
        private readonly TenantSystemStorage _tenantSystemStorage;

        /// <summary />
        public FollowPerimeterController(PublicPrivateKeyService publicPrivatePublicKeyService, FollowerPerimeterService followerPerimeterService, TenantSystemStorage tenantSystemStorage)
        {
            _publicPrivatePublicKeyService = publicPrivatePublicKeyService;
            _followerPerimeterService = followerPerimeterService;
            _tenantSystemStorage = tenantSystemStorage;
        }

        /// <summary />
        [HttpPost("follow")]
        public async Task<IActionResult> ReceiveFollowRequest([FromBody] EccEncryptedPayload payload)
        {
            OdinValidationUtils.AssertNotNull(payload, nameof(payload));

            var db = _tenantSystemStorage.IdentityDatabase;
<<<<<<< HEAD
            var payloadBytes = await _publicPrivatePublicKeyService.EccDecryptPayload(PublicPrivateKeyType.OfflineKey, payload, WebOdinContext, db);
=======
            var (isValidPublicKey, payloadBytes) = await _publicPrivatePublicKeyService.RsaDecryptPayload(PublicPrivateKeyType.OfflineKey, payload, WebOdinContext, db);
            if (isValidPublicKey == false)
            {
                //TODO: extend with error code indicated a bad public key 
                return BadRequest("Invalid Public Key");
            }
>>>>>>> main

            var request = OdinSystemSerializer.Deserialize<PerimeterFollowRequest>(payloadBytes.ToStringFromUtf8Bytes());
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertIsValidOdinId(request.OdinId, out _);

            await _followerPerimeterService.AcceptFollower(request, WebOdinContext, db);

            return Ok();
        }

        /// <summary />
        [HttpPost("unfollow")]
        public async Task<IActionResult> ReceiveUnfollowRequest()
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _followerPerimeterService.AcceptUnfollowRequest(WebOdinContext, db);
            return Ok();
        }
    }
}