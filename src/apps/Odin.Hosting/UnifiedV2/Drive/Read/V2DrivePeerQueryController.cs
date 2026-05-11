using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerByUniqueId)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DrivePeerQueryByUidController(PeerDriveQueryService peerDriveQueryService) : OdinControllerBase
    {
        [HttpGet("exists")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<FileExistsOnPeerResponse> GetExists(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid uid)
        {
            return await peerDriveQueryService.FileExistsOnRemoteByUniqueId(
                (OdinId)odinId, driveId, uid, WebOdinContext);
        }
    }

    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerByGtid)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DrivePeerQueryByGtidController(PeerDriveQueryService peerDriveQueryService) : OdinControllerBase
    {
        [HttpGet("exists")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<FileExistsOnPeerResponse> GetExists(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromRoute] Guid gtid)
        {
            return await peerDriveQueryService.FileExistsOnRemoteByGlobalTransitId(
                (OdinId)odinId, driveId, gtid, WebOdinContext);
        }
    }
}
