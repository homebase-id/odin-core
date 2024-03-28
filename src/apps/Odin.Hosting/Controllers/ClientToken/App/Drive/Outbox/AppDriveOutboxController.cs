using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive.Outbox;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Controllers.ClientToken.App.Drive.Outbox
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.DriveOutboxV1)]
    [AuthorizeValidAppToken]
    public class AppDriveOutboxController(PeerOutbox peerOutbox) : OutboxControllerBase(peerOutbox)
    {
    }
}