using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
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
        public async Task<IActionResult> DeleteFilesByGroupIdBatch(Guid driveId, [FromBody] DeleteFilesByGroupIdBatchRequestV2 batchRequest)
        {
            var deleteBatchFinalResult = new DeleteFilesByGroupIdBatchResult()
            {
                Results = new List<DeleteFileByGroupIdResult>()
            };
            
            WebOdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);

            foreach (var request in batchRequest.Requests)
            {
                var groupIdToBeDeleted = request.GroupId;

                var qp = new FileQueryParams()
                {
                    TargetDrive = new TargetDrive(),
                    GroupId = new List<Guid>() { groupIdToBeDeleted }
                };

                var options = new QueryBatchResultOptions()
                {
                    IncludeHeaderContent = false,
                    MaxRecords = int.MaxValue
                };

                var queryResults = await GetHttpFileSystemResolver().ResolveFileSystem(batchRequest.FileSystemType)
                    .Query.GetBatch(driveId, qp, options, WebOdinContext);

                //
                // Delete the batch resulting from the query
                //
                var deleteBatch = new DeleteFileIdBatchRequest()
                {
                    Requests = queryResults.SearchResults.Select(sr => new DeleteFileRequest()
                    {
                        File = new ExternalFileIdentifier()
                        {
                            FileId = sr.FileId,
                            TargetDrive = request.TargetDrive
                        },
                        Recipients = request.Recipients
                    }).ToList()
                };

                var batchResults = await PerformDeleteFileIdBatch(deleteBatch);

                deleteBatchFinalResult.Results.Add(new DeleteFileByGroupIdResult()
                {
                    GroupId = groupIdToBeDeleted,
                    DeleteFileResults = batchResults.Results
                });
            }

            return new JsonResult(deleteBatchFinalResult);
        }
        
        /// <summary>
        /// Deletes multiple files in a single batch operation.
        /// </summary>
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