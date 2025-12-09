using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
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
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerPeerQueryController(
        PeerDriveQueryService peerDriveQueryService) : PeerQueryControllerBase(peerDriveQueryService);
}