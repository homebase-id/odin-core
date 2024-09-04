using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.Peer;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Controllers.PeerIncoming.Membership
{
    [ApiController]
    [Route(PeerApiPathConstants.InvitationsV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class ConnectionsController(
        CircleNetworkService circleNetwork,
        TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        [HttpPost("verify-identity-connection")]
        public async Task<IActionResult> VerifyConnection()
        {
            using var cn = tenantSystemStorage.CreateConnection();
            var code = await circleNetwork.VerifyConnectionCode(WebOdinContext, cn);
            return new JsonResult(code);
        }
    }
}