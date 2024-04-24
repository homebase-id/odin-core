using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Controllers.Base.Drive.Status;

public abstract class DriveStatusControllerBase(StandardFileSystem fileSystem, IPeerOutbox peerOutbox, TransitInboxBoxStorage peerInbox) : OdinControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(Guid alias, Guid type)
    {
        var driveId = WebOdinContext.PermissionsContext.GetDriveId(new TargetDrive()
        {
            Alias = alias,
            Type = type
        });

        var status = new DriveStatus()
        {
            Inbox = await peerInbox.GetStatus(driveId),
            Outbox = await peerOutbox.GetOutboxStatus(driveId),
            SizeInfo = await fileSystem.Query.GetDriveSize(driveId, WebOdinContext)
        };

        return new JsonResult(status);
    }
}