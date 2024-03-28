using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive.Outbox;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Controllers.OwnerToken.Drive.Outbox
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveOutboxV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveOutboxController(PeerOutbox peerOutbox) : OutboxControllerBase(peerOutbox)
    {
    }
}