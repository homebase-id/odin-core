using Microsoft.AspNetCore.Mvc;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.PeerSenderV1)]
    [AuthorizeValidAppToken]
    public class AppPeerSenderController(IPeerOutgoingTransferService peerOutgoingTransferService) : PeerSenderControllerBase(peerOutgoingTransferService);
}