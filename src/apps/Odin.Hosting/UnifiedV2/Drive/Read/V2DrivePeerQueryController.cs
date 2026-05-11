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
    [Route(UnifiedApiRouteConstants.PeerFilesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DrivePeerQueryController(
        PeerDriveQueryService peerDriveQueryService)
        : OdinControllerBase
    {
        [HttpPost("file-exists")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<bool> GetFileExists([FromRoute] Guid driveId, [FromBody] PeerFileExistsByUidAndVersionTagRequest request)
        {
            return await peerDriveQueryService.FileExistsOnRemote(driveId, request, WebOdinContext);
        }
    }
}