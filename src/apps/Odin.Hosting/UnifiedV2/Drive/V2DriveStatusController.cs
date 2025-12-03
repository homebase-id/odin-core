using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Drive.Status;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.UnifiedV2.Drive;

[ApiController]
[Route(UnifiedApiRouteConstants.Drive)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
public class V2DriveStatusController(
    StandardFileSystem fileSystem,
    PeerOutbox peerOutbox,
    TransitInboxBoxStorage peerInbox,
    PeerOutgoingTransferService peerOutgoingTransferService)
    : DriveStorageControllerBase(peerOutgoingTransferService)
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(Guid driveId)
    {
        WebOdinContext.Caller.AssertCallerIsOwner();
        
        var status = new DriveStatus()
        {
            Inbox = await peerInbox.GetStatusAsync(driveId),
            Outbox = await peerOutbox.GetOutboxStatusAsync(driveId),
            SizeInfo = await fileSystem.Query.GetDriveSize(driveId, WebOdinContext)
        };

        return new JsonResult(status);
    }
}