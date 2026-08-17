using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.DrivesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveBatchQueryController(
        PeerOutgoingTransferService peerOutgoingTransferService,
        ILogger<V2DriveControllerBase> logger,
        V2BatchCollectionQueryService batchCollectionQueryService) :
        V2DriveControllerBase(peerOutgoingTransferService, logger)
    {
        /// <summary>
        /// Runs several drive queries in one call.  A section-level fault (unknown drive, archived drive, no read
        /// grant) never fails the collection: it comes back as a section with a
        /// <see cref="QueryBatchSectionStatus"/> saying why.  <see cref="QueryBatchCollectionRequestV2.MaxRecords"/>
        /// is a budget for the whole call, filled in request order.
        /// </summary>
        [HttpPost("query-batch-collection")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<QueryBatchCollectionResponseV2> QueryBatchCollection([FromBody] QueryBatchCollectionRequestV2 request)
        {
            // Sections may override this individually, so a single collection can mix Standard and Comment.
            var defaultFileSystemType = GetFileSystemType();
            return await batchCollectionQueryService.GetBatchCollectionAsync(request, defaultFileSystemType, WebOdinContext);
        }
    }
}
