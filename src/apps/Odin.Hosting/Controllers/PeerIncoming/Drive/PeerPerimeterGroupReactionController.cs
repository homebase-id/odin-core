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
    public class PeerPerimeterGroupReactionController(
        PeerIncomingGroupReactionInboxRouterService groupReactionInboxRouterService,
        TenantSystemStorage tenantSystemStorage)
        : OdinControllerBase
    {
        [HttpPost("add")]
        public async Task<PeerResponseCode> AddReactionContent(RemoteReactionRequestRedux request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            return await groupReactionInboxRouterService.AddReaction(request, WebOdinContext, db);
        }

        [HttpPost("delete")]
        public async Task<PeerResponseCode> DeleteReactionContent([FromBody] RemoteReactionRequestRedux request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            return await groupReactionInboxRouterService.DeleteReaction(request, WebOdinContext, db);
        }
    }
}