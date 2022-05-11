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
    [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = PerimeterAuthConstants.TransitCertificateAuthScheme)]
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
            var payloadBytes = await _rsaPublicKeyService.DecryptPayloadUsingOfflineKey(payload);
            ConnectionRequest request = JsonConvert.DeserializeObject<ConnectionRequest>(payloadBytes.ToStringFromUTF8Bytes());

            await _circleNetworkRequestService.ReceiveConnectionRequest(request);
            return new JsonResult(new NoResultResponse(true));
        }


        [HttpPost("establishconnection")]
        public async Task<IActionResult> EstablishConnection([FromBody] RsaEncryptedPayload payload)
        {
            var payloadBytes = await _rsaPublicKeyService.DecryptPayloadUsingOfflineKey(payload);
            ConnectionRequestReply reply = JsonConvert.DeserializeObject<ConnectionRequestReply>(payloadBytes.ToStringFromUTF8Bytes());

            await _circleNetworkRequestService.EstablishConnection(reply);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}