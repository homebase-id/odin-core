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

namespace Odin.Hosting.UnifiedV2.Drive.Write
{
    /// <summary>
    /// Operations on multiple files at a time
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.FilesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveBatchFileWriteOperationsController(PeerOutgoingTransferService peerOutgoingTransferService) :
        V2DriveControllerBase(peerOutgoingTransferService)
    {
        /// <summary>
        /// Sends a read receipt for files matching a query by end time.
        /// </summary>
        [SwaggerOperation(
            Summary = "Send read receipt by time query",
            Description = "Sends a read receipt for all files on the drive matching the given FileType, DataType, " +
                          "and/or GroupId that were created on or before EndTime. " +
                          "An optional Timestamp can specify when the files were actually read (e.g. for offline scenarios). " +
                          "If Timestamp is omitted and the file is already marked as read, it is skipped (no unnecessary update). " +
                          "If Timestamp is provided, it is clamped to min(Timestamp, now) and only applied when it is " +
                          "later than the file's current read time.",
            Tags = [SwaggerInfo.FileTransfer]
        )]
        [HttpPost("send-read-receipt-batch-by-time")]
        public async Task<SendReadReceiptResultV2> SendReadReceiptByTime(Guid driveId, [FromBody] SendReadReceiptByEndTimeRequestV2 request)
        {
            var queryParams = new FileQueryParams()
            {
                GroupId = request.GroupId.HasValue ? [request.GroupId.Value] : null,
                FileType = request.FileType.HasValue ? [request.FileType.Value] : null,
                DataType = request.DataType.HasValue ? [request.DataType.Value] : null,
            };

            var v1Result = await PeerOutgoingTransferService.SendReadReceipt(
                driveId,
                queryParams,
                request.EndTime,
                WebOdinContext,
                base.GetFileSystemType(),
                request.Timestamp);

            return new SendReadReceiptResultV2
            {
                Results = v1Result.Results.Select(v1Item => new SendReadReceiptResultFileItemV2
                {
                    FileId = v1Item.File.FileId,
                    Status = v1Item.Status
                }).ToList()
            };
        }

        /// <summary>
        /// Sends a read receipt for one or more specific files by ID.
        /// </summary>
        [SwaggerOperation(
            Summary = "Send read receipt for one or more files",
            Description = "Marks the specified files as read and notifies the original sender via the peer transfer service. " +
                          "An optional Timestamp can specify when the files were actually read (e.g. for offline scenarios). " +
                          "If Timestamp is omitted and a file is already marked as read, it is skipped (no unnecessary update). " +
                          "If Timestamp is provided, it is clamped to min(Timestamp, now) and only applied when it is " +
                          "later than the file's current read time.",
            Tags = [SwaggerInfo.FileTransfer]
        )]
        [HttpPost("send-read-receipt-batch")]
        public async Task<SendReadReceiptResultV2> SendReadReceipt(Guid driveId, [FromBody] SendReadReceiptRequestV2 request)
        {
            var internalFiles = request.Files.Select(fileId => new InternalDriveFileId(driveId, fileId)).ToList();
            var v1Result = await PeerOutgoingTransferService.SendReadReceipt(internalFiles,
                WebOdinContext,
                base.GetFileSystemType(),
                request.Timestamp);

            return new SendReadReceiptResultV2
            {
                Results = v1Result.Results.Select(v1Item => new SendReadReceiptResultFileItemV2
                {
                    FileId = v1Item.File.FileId,
                    Status = v1Item.Status
                }).ToList()
            };
        }

        [HttpPost("delete-batch/by-group-id")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileWrite])]
        public async Task<DeleteFilesByGroupIdBatchResultV2> DeleteFilesByGroupIdBatch(Guid driveId,
            [FromBody] DeleteFilesByGroupIdBatchRequestV2 batchRequest)
        {
            var deleteBatchFinalResult = new DeleteFilesByGroupIdBatchResultV2()
            {
                Results = []
            };

            WebOdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);

            foreach (var request in batchRequest.Requests)
            {
                var groupIdToBeDeleted = request.GroupId;

                var qp = new FileQueryParamsV1()
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

            return deleteBatchFinalResult;
        }

        /// <summary>
        /// Deletes multiple files in a single batch operation.
        /// </summary>
        [HttpPost("delete-batch/by-file-id")]
        [SwaggerOperation(
            Summary = "Batch delete files",
            Description = "Deletes multiple files in a single API call.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        public async Task<DeleteFileIdBatchResultV2> DeleteFileIdBatch(Guid driveId, [FromBody] DeleteFileIdBatchRequestV2 request)
        {
            WebOdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);

            var batchResult = new DeleteFileIdBatchResultV2()
            {
                Results = await DeleteFileIdBatchInternal(driveId, request.Requests)
            };

            return batchResult;
        }
    }
}