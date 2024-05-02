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
    [Route(AppApiPathConstants.DriveV1)]
    [AuthorizeValidAppToken]
    public class AppDriveDriveStatusController(
        StandardFileSystem fileSystem,
        IPeerOutbox peerOutbox,
        TransitInboxBoxStorage peerInbox,
        TenantSystemStorage tenantSystemStorage) : DriveStatusControllerBase(fileSystem, peerOutbox, peerInbox, tenantSystemStorage)
    {
    }
}