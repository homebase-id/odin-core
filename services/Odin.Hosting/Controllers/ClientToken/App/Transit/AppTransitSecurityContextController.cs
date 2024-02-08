using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Drive.Query;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    [ApiController]
    [Route(AppApiPathConstants.PeerQueryV1)]
    [AuthorizeValidAppToken]
    public class AppTransitSecurityContextController(PeerQueryService peerQueryService) : TransitSecurityContextControllerBase(peerQueryService);
}
