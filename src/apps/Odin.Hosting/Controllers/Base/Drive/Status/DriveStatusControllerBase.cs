using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Controllers.Base.Drive.Status;

public abstract class DriveStatusControllerBase(
    StandardFileSystem fileSystem,
    PeerOutbox peerOutbox,
    TransitInboxBoxStorage peerInbox,
    TenantSystemStorage tenantSystemStorage) : OdinControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(Guid alias, Guid type)
    {
        var driveId = WebOdinContext.PermissionsContext.GetDriveId(new TargetDrive()
        {
            Alias = alias,
            Type = type
        });

        using var cn = tenantSystemStorage.CreateConnection();
        var status = new DriveStatus()
        {
            Inbox = await peerInbox.GetStatus(driveId, cn),
            Outbox = await peerOutbox.GetOutboxStatus(driveId, cn),
            SizeInfo = await fileSystem.Query.GetDriveSize(driveId, WebOdinContext, cn)
        };

        return new JsonResult(status);
    }
}