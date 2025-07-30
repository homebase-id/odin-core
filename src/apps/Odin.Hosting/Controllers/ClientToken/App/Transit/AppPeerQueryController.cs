using Microsoft.AspNetCore.Mvc;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.ClientToken.Shared;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(AppApiPathConstants.PeerQueryV1)]
    // [AuthorizeValidAppToken]

    //test
    [Route(GuestApiPathConstants.PeerQueryV1)]
    [AuthorizeValidGuestOrAppToken]
    public class AppPeerQueryController(PeerDriveQueryService peerDriveQueryService)
        : PeerQueryControllerBase(peerDriveQueryService);
}
