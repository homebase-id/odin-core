using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Transit
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstantsV1.PeerSenderV1)]
    [Route(GuestApiPathConstantsV1.PeerSenderV1)]
    [AuthorizeValidGuestOrAppToken]
    public class ClientTokenPeerSenderController(ILogger<ClientTokenPeerSenderController> logger, PeerOutgoingTransferService peerOutgoingTransferService, DriveManager driveManager) :
        PeerSenderControllerBase(logger, peerOutgoingTransferService, driveManager);
}