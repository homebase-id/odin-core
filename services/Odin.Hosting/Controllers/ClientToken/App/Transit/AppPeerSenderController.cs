using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Transfer;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.PeerSenderV1)]
    [AuthorizeValidAppToken]
    public class AppPeerSenderController(IPeerTransferService peerTransferService) : PeerSenderControllerBase(peerTransferService);
}