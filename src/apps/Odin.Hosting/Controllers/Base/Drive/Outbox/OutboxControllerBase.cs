using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Controllers.Base.Drive.Outbox;

public abstract class OutboxControllerBase(PeerOutbox peerOutbox) : OdinControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(Guid alias, Guid type)
    {
        var driveId = OdinContext.PermissionsContext.GetDriveId(new TargetDrive()
        {
            Alias = alias,
            Type = type
        });

        return new JsonResult(await peerOutbox.GetOutboxStatus(driveId));
    }
}