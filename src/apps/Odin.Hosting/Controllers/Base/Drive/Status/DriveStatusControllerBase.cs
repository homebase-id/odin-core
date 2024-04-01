using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Controllers.Base.Drive.Status;

public abstract class DriveStatusControllerBase(StandardFileSystem fileSystem, PeerOutbox peerOutbox, PeerInbox peerInbox) : OdinControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(Guid alias, Guid type)
    {
        var driveId = OdinContext.PermissionsContext.GetDriveId(new TargetDrive()
        {
            Alias = alias,
            Type = type
        });

        var status = new DriveStatus()
        {
            Inbox = await peerInbox.GetStatus(driveId),
            Outbox = await peerOutbox.GetOutboxStatus(driveId),
            SizeInfo = await fileSystem.Query.GetDriveSize(driveId)
        };

        return new JsonResult(status);
    }
}