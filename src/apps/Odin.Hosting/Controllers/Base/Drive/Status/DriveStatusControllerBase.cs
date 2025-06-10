using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Identity;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Controllers.Base.Drive.Status;

public abstract class DriveStatusControllerBase(
    StandardFileSystem fileSystem,
    PeerOutbox peerOutbox,
    TransitInboxBoxStorage peerInbox) : OdinControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(Guid alias, Guid type)
    {
        var driveId = alias;
        var status = new DriveStatus()
        {
            Inbox = await peerInbox.GetStatusAsync(driveId),
            Outbox = await peerOutbox.GetOutboxStatusAsync(driveId),
            SizeInfo = await fileSystem.Query.GetDriveSize(driveId, WebOdinContext)
        };

        return new JsonResult(status);
    }

    [HttpGet("outbox-item")]
    public async Task<IActionResult> GetOutboxItem(Guid alias, Guid fileId, string recipient)
    {
        var driveId = alias;
        var item = await peerOutbox.GetItemAsync(driveId, fileId, (OdinId)recipient);
        return new JsonResult(item);
    }
}