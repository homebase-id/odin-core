using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
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

        [HttpPost("make-introduction")]
        public async Task<IActionResult> ReceiveIntroduction([FromBody] SharedSecretEncryptedPayload payload)
        {
            var cn = tenantSystemStorage.IdentityDatabase;
            await introductionService.ReceiveIntroductions(payload, WebOdinContext, cn);
            return Ok();
        }
    }
}