using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive
{
    /// <summary>
    /// Operations on multiple files at a time
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.FilesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveFileBatchOperationsController(PeerOutgoingTransferService peerOutgoingTransferService) :
        V2DriveControllerBase(peerOutgoingTransferService)
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
            var result = await PeerOutgoingTransferService.SendReadReceipt(internalFiles,
                WebOdinContext,
                base.GetFileSystemType());
            return new JsonResult(result);
        }

        [HttpPost("delete-batch/by-group-id")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileDelete])]
        public async Task<IActionResult> DeleteFilesByGroupIdBatch(Guid driveId, [FromBody] DeleteFilesByGroupIdBatchRequestV2 batchRequest)
        {
            var deleteBatchFinalResult = new DeleteFilesByGroupIdBatchResultV2()
            {
                Results = []
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

                var queryResults = await GetFileSystem().Query.GetBatch(driveId, qp, options, WebOdinContext);

                //
                // Delete the batch resulting from the query
                //
                var requests = queryResults.SearchResults.Select(sr => new DeleteFileRequestV2()
                {
                    FileId = sr.FileId,
                    Recipients = request.Recipients
                }).ToList();

                var batchResults = await DeleteFileIdBatchInternal(driveId, requests);

                deleteBatchFinalResult.Results.Add(new DeleteFileByGroupIdResultV2()
                {
                    GroupId = groupIdToBeDeleted,
                    DeleteFileResults = batchResults
                });
            }

            return new JsonResult(deleteBatchFinalResult);
        }

        /// <summary>
        /// Deletes multiple files in a single batch operation.
        /// </summary>
        [HttpPost("delete-batch/by-file-id")]
        [SwaggerOperation(
            Summary = "Batch delete files",
            Description = "Deletes multiple files in a single API call.",
            Tags = [SwaggerInfo.FileDelete]
        )]
        public async Task<IActionResult> DeleteFileIdBatch(Guid driveId, [FromBody] DeleteFileIdBatchRequestV2 request)
        {
            WebOdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);

            var batchResult = new DeleteFileIdBatchResultV2()
            {
                Results = await DeleteFileIdBatchInternal(driveId, request.Requests)
            };

            return new JsonResult(batchResult);
        }
    }
}