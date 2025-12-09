using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.FilesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveStorageController(PeerOutgoingTransferService peerOutgoingTransferService)
        : DriveStorageControllerBase(peerOutgoingTransferService)
    {
        [HttpPost("delete-batch-by-group-id")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileDelete])]
        public new async Task<IActionResult> DeleteFilesByGroupIdBatch([FromBody] DeleteFilesByGroupIdBatchRequest request)
        {
            return await base.DeleteFilesByGroupIdBatch(request);
        }
        
        /// <summary>
        /// Deletes multiple files in a single batch operation.
        /// </summary>
        /// <remarks>
        /// <b>Why POST instead of DELETE?</b><br/>
        /// Batch deletion requires structured JSON input (list of file IDs).  
        /// DELETE bodies are widely unsupported or inconsistently handled across HTTP stacks.
        /// Using POST avoids:
        /// <ul>
        /// <li>Proxies stripping DELETE bodies</li>
        /// <li>Swagger/OpenAPI generation inconsistencies</li>
        /// <li>Load balancer incompatibilities</li>
        /// <li>Model binding failures in clients</li>
        /// </ul>
        /// This is the recommended and stable pattern for APIs across the industry.
        /// </remarks>
        /// <param name="request">List of file IDs to delete.</param>
        /// <returns>204 on success.</returns>
        [HttpPost("delete-batch")]
        [SwaggerOperation(
            Summary = "Batch delete files",
            Description = "Deletes multiple files in a single API call.",
            Tags = [SwaggerInfo.FileDelete]
        )]
        public new async Task<IActionResult> DeleteFileIdBatch([FromBody] DeleteFileIdBatchRequest request)
        {
            return await base.DeleteFileIdBatch(request);
        }
    }
}