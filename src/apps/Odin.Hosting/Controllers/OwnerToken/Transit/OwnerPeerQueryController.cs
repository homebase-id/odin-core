using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.PeerQueryV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerPeerQueryController(PeerDriveQueryOutgoingService peerDriveQueryOutgoingService) : PeerQueryControllerBase(peerDriveQueryOutgoingService);
}