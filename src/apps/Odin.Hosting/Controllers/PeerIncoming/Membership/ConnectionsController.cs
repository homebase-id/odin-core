using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.Peer;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Verification;
using Refit;

namespace Odin.Hosting.Controllers.PeerIncoming.Membership
{
    [ApiController]
    [Route(PeerApiPathConstants.InvitationsV1)]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork,
        AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class ConnectionsController(
        CircleNetworkService circleNetwork,
        CircleNetworkVerificationService verificationService) : OdinControllerBase
    {
        [HttpPost("verify-identity-connection")]
        public async Task<IActionResult> VerifyConnection()
        {
            var code = await circleNetwork.VerifyConnectionCode(WebOdinContext);
            return new JsonResult(code);
        }

        [HttpPost("update-remote-verification-hash")]
        public async Task<IActionResult> UpdateRemoteVerificationHash([Body] SharedSecretEncryptedPayload payload)
        {
            await verificationService.SynchronizeVerificationHashFromRemote(payload, WebOdinContext);
            return Ok();
        }
    }
}