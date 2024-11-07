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

        var db = tenantSystemStorage.IdentityDatabase;
        var status = new DriveStatus()
        {
            Inbox = await peerInbox.GetStatusAsync(driveId),
            Outbox = await peerOutbox.GetOutboxStatusAsync(driveId, db),
            SizeInfo = await fileSystem.Query.GetDriveSize(driveId, WebOdinContext, db)
        };

        return new JsonResult(status);
    }
}