using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive
{
    /// <summary>
    /// Operations on multiple files at a time
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.DrivesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveFileBatchOperationsController(PeerOutgoingTransferService peerOutgoingTransferService) : OdinControllerBase
    {
        /// <summary>
        /// Sends a read receipt for a file.
        /// </summary>
        [SwaggerOperation(
            Summary = "Send read receipt",
            Description = "Sends a read receipt to the peer transfer service.",
            Tags = [SwaggerInfo.FileTransfer]
        )]
        [HttpPost("send-read-receipt")]
        public async Task<IActionResult> SendReadReceipt(Guid driveId, [FromBody] SendReadReceiptRequestV2 request)
        {
            var internalFiles = request.Files.Select(fileId => new InternalDriveFileId(driveId, fileId)).ToList();
            var result = await peerOutgoingTransferService.SendReadReceipt(internalFiles,
                WebOdinContext,
                request.FileSystemType);
            return new JsonResult(result);
        }
    }
}