using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Fluff;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.PeerIncoming.Membership
{
    [ApiController]
    [Route(PeerApiPathConstants.InvitationsV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class IntroductionsController(
        CircleNetworkIntroductionService introductionService,
        TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        [HttpPost("request-introductions")]
        public async Task<IActionResult> ReceiveIntroductionRequest([FromBody] SharedSecretEncryptedPayload payload)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await introductionService.ReceiveIntroductionRequest(payload, WebOdinContext, cn);
            return Ok();
        }

        [HttpPost("make-introduction")]
        public async Task<IActionResult> ReceiveIntroduction([FromBody] SharedSecretEncryptedPayload payload)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await introductionService.ReceiveIntroduction(payload, WebOdinContext, cn);
            return Ok();
        }
    }
}