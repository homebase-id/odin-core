using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Inbox;

[ApiController]
[Route(UnifiedApiRouteConstants.ByDriveId)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DriveInboxController(PeerInboxProcessor peerInboxProcessor) : OdinControllerBase
{
    [HttpGet("process")]
    [SwaggerOperation(Tags = [SwaggerInfo.DriveStatus])]
    public async Task<InboxStatus> ProcessInbox(Guid driveId, [FromQuery] int batchSize = 10)
    {
        var targetDrive = WebOdinContext.PermissionsContext.GetTargetDrive(driveId);
            
        var result = await peerInboxProcessor.ProcessInboxAsync(targetDrive, WebOdinContext, batchSize);
        return result;
    }
}