using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Peer;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Base;
using Odin.Services.Peer.Incoming.Drive.Reactions.Group;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Controllers.PeerIncoming.Drive
{
    [ApiController]
    [Route(PeerApiPathConstants.GroupReactionsV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    [ApiExplorerSettings(GroupName = "peer-v1")]
    public class PeerPerimeterGroupReactionController(
        PeerIncomingGroupReactionInboxRouterService groupReactionInboxRouterService)
        : OdinControllerBase
    {
        [HttpPost("add")]
        public async Task<PeerResponseCode> AddReactionContent(RemoteReactionRequestRedux request)
        {
            
            return await groupReactionInboxRouterService.AddReaction(request, WebOdinContext);
        }

        [HttpPost("delete")]
        public async Task<PeerResponseCode> DeleteReactionContent([FromBody] RemoteReactionRequestRedux request)
        {
            
            return await groupReactionInboxRouterService.DeleteReaction(request, WebOdinContext);
        }
    }
}