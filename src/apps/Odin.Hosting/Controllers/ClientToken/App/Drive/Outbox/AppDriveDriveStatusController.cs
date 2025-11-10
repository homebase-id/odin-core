using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive.Status;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Controllers.ClientToken.App.Drive.Outbox
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstantsV1.DriveV1)]
    [AuthorizeValidAppToken]
    public class AppDriveDriveStatusController(
        StandardFileSystem fileSystem,
        PeerOutbox peerOutbox,
        TransitInboxBoxStorage peerInbox) : DriveStatusControllerBase(fileSystem, peerOutbox, peerInbox)
    {
    }
}