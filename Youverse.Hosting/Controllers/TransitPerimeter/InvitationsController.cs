using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Hosting.Authentication.CertificatePerimeter;
using Youverse.Hosting.Authentication.Perimeter;

namespace Youverse.Hosting.Controllers.TransitPerimeter
{
    /// <summary>
    /// Controller which accepts various invitations.  This controller 
    /// must only add invitations and make no other changes.
    /// </summary>
    [ApiController]
    [Route("api/perimeter/invitations")]

    //so here i could change the transit to have two policies - one that requires an app and one that is an certificate only
    //how do you know it is the owner console tho?
    [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = PerimeterAuthConstants.PublicTransitAuthScheme)]
    public class InvitationsController : ControllerBase
    {
        private readonly ICircleNetworkRequestService _circleNetworkRequestService;
        private IPublicKeyService _rsaPublicKeyService;

        public InvitationsController(ICircleNetworkRequestService circleNetworkRequestService, IPublicKeyService rsaPublicKeyService)
        {
            _circleNetworkRequestService = circleNetworkRequestService;
            _rsaPublicKeyService = rsaPublicKeyService;
        }

        [HttpPost("connect")]
        public async Task<IActionResult> ReceiveConnectionRequest([FromBody] RsaEncryptedPayload payload)
        {
            Console.Write("ReceiveConnectionRequest 0");

            var (isValidPublicKey, payloadBytes) = await _rsaPublicKeyService.DecryptPayloadUsingOfflineKey(payload);

            if (isValidPublicKey == false)
            {
                //TODO: extend with error code indicated a bad public key 
                return new JsonResult(new NoResultResponse(false));
            }

            ConnectionRequest request = JsonConvert.DeserializeObject<ConnectionRequest>(payloadBytes.ToStringFromUTF8Bytes());

            Console.Write("ReceiveConnectionRequest 1");

            await _circleNetworkRequestService.ReceiveConnectionRequest(request);
            Console.Write("ReceiveConnectionRequest 2");

            return new JsonResult(new NoResultResponse(true));
        }


        [HttpPost("establishconnection")]
        public async Task<IActionResult> EstablishConnection([FromBody] RsaEncryptedPayload payload)
        {
            var (isValidPublicKey, payloadBytes) = await _rsaPublicKeyService.DecryptPayloadUsingOfflineKey(payload);

            if (isValidPublicKey == false)
            {
                //TODO: extend with error code indicated a bad public key 
                return new JsonResult(new NoResultResponse(false));
            }
            
            ConnectionRequestReply reply = JsonConvert.DeserializeObject<ConnectionRequestReply>(payloadBytes.ToStringFromUTF8Bytes());

            await _circleNetworkRequestService.EstablishConnection(reply);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}