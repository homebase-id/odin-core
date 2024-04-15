using Microsoft.AspNetCore.Mvc;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(AppApiPathConstants.PeerQueryV1)]
    [AuthorizeValidAppToken]
    public class AppPeerSecurityContextController(PeerDriveQueryOutgoingService peerDriveQueryOutgoingService) : PeerSecurityContextControllerBase(peerDriveQueryOutgoingService);
}
