using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.Peer;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.PeerIncoming
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.SecurityV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    [ApiExplorerSettings(GroupName = "peer-v1")]
    public class PeerPerimeterSecurityController() : OdinControllerBase
    {
        /// <summary />
        [HttpGet("context")]
        public Task<RedactedOdinContext> GetRemoteSecurityContext()
        {
            return Task.FromResult(WebOdinContext.Redacted());
        }
    }
}