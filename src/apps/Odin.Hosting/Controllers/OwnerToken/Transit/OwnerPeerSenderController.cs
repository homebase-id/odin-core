using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Transit;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.PeerSenderV1)]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerPeerSenderController : PeerSenderControllerBase
    {
        public OwnerPeerSenderController(
            ILogger<OwnerPeerSenderController> logger,
            PeerOutgoingTransferService peerOutgoingTransferService) : base(logger, peerOutgoingTransferService)
        {
        }
    }
}