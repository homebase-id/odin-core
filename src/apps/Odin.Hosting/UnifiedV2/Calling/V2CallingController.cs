using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Calling;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Calling
{
    /// <summary>
    /// What a device puts into <c>iceServers</c> right before it creates a call's peer connection, and
    /// again before an ICE restart: this host's STUN, and a short-lived TURN login when the host runs a
    /// TURN server. A POST because it names the peer and must never be cached. See docs/turn.md.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.Calling)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2CallingController(IceServerService iceServerService) : OdinControllerBase
    {
        [HttpPost("ice-servers")]
        [SwaggerOperation(Tags = [SwaggerInfo.Notifications])]
        public async Task<IceServersResponse> GetIceServers([FromBody] IceServersRequest request)
        {
            Response.Headers.CacheControl = "no-store";
            return await iceServerService.GetIceServersAsync(request, WebOdinContext);
        }
    }
}
