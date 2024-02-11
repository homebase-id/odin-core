using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Identity;
using Odin.Core.Services.Base;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Drive.Query;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Base.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    public class TransitSecurityContextControllerBase(PeerQueryService peerQueryService) : OdinControllerBase
    {
        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpPost("security/context")]
        public async Task<RedactedOdinContext> GetRemoteDotYouContext([FromBody] TransitGetSecurityContextRequest request)
        {
            var ctx = await peerQueryService.GetRemoteDotYouContext((OdinId)request.OdinId);
            return ctx;
        }
    }
}
