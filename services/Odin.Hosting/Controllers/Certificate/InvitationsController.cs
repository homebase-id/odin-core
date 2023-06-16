using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Fluff;
using Odin.Core.Serialization;
using Odin.Core.Services.Contacts.Circle.Requests;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Hosting.Authentication.Perimeter;

namespace Odin.Hosting.Controllers.Certificate
{
    /// <summary>
    /// Controller which accepts various invitations.  This controller 
    /// must only add invitations and make no other changes.
    /// </summary>
    [ApiController]
    [Route("api/perimeter/invitations")]

    //so here i could change the transit to have two policies - one that requires an app and one that is an certificate only
    //how do you know it is the owner console tho?
    [Authorize(Policy = CertificatePerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PerimeterAuthConstants.PublicTransitAuthScheme)]
    public class InvitationsController : ControllerBase
    {
        private readonly CircleNetworkRequestService _circleNetworkRequestService;
        private readonly RsaKeyService _rsaKeyService;

        public InvitationsController(CircleNetworkRequestService circleNetworkRequestService, RsaKeyService rsaKeyService)
        {
            _circleNetworkRequestService = circleNetworkRequestService;
            _rsaKeyService = rsaKeyService;
        }

        [HttpPost("connect")]
        public async Task<IActionResult> ReceiveConnectionRequest([FromBody] RsaEncryptedPayload payload)
        {
            // var (isValidPublicKey, payloadBytes) = await _rsaPublicKeyService.DecryptPayload(RsaKeyType.OfflineKey, payload);
            // if (isValidPublicKey == false)
            // {
            //     //TODO: extend with error code indicated a bad public key 
            //     return new JsonResult(new NoResultResponse(false));
            // }
            //
            // // To use an only key, we need to store most of the payload encrypted but need to know who it's from
            // ConnectionRequest request = OdinSystemSerializer.Deserialize<ConnectionRequest>(payloadBytes.ToStringFromUtf8Bytes());
            
            await _circleNetworkRequestService.ReceiveConnectionRequest(payload);
            return new JsonResult(new NoResultResponse(true));
        }


        [HttpPost("establishconnection")]
        public async Task<IActionResult> EstablishConnection([FromBody] RsaEncryptedPayload payload)
        {
            var (isValidPublicKey, payloadBytes) = await _rsaKeyService.DecryptPayload(RsaKeyType.OfflineKey, payload);

            if (isValidPublicKey == false)
            {
                //TODO: extend with error code indicated a bad public key 
                return new JsonResult(new NoResultResponse(false));
            }

            ConnectionRequestReply reply = OdinSystemSerializer.Deserialize<ConnectionRequestReply>(payloadBytes.ToStringFromUtf8Bytes());

            await _circleNetworkRequestService.EstablishConnection(reply);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}