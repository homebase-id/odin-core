using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.PeerQueryV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerPeerSecurityContextController : PeerSecurityContextControllerBase
    {
        public OwnerPeerSecurityContextController(
            PeerDriveQueryService peerDriveQueryService):base(peerDriveQueryService)
        {
        }
    }
}
