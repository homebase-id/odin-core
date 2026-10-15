using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;
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
    public class V2DriveBatchFileWriteOperationsController(
        PeerOutgoingTransferService peerOutgoingTransferService,
        ILogger<V2DriveControllerBase> logger) :
        V2DriveControllerBase(peerOutgoingTransferService, logger)
    {
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

        /// <summary>
        /// Sets only the archival status of up to <see cref="UpdateArchivalStatusBatchRequestV2.MaxFiles"/> files.
        /// </summary>
        [HttpPost("update-archival-status-batch")]
        [SwaggerOperation(
            Summary = "Set the archival status of many files",
            Description = "Sets appData.archivalStatus on each file and leaves the rest of its header as it is, so " +
                          "the caller needs neither the key header nor the content. Each file is updated only if its " +
                          "version tag still matches, and gets a new version tag and modified time. Results are per " +
                          "file: a stale, missing or deleted file reports its error code and does not stop the others.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        public async Task<UpdateArchivalStatusBatchResultV2> UpdateArchivalStatusBatch(Guid driveId,
            [FromBody] UpdateArchivalStatusBatchRequestV2 request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertNotNull(request.Files, nameof(request.Files));
            OdinValidationUtils.AssertIsTrue(request.Files.Count is > 0 and <= UpdateArchivalStatusBatchRequestV2.MaxFiles,
                $"files must hold 1 to {UpdateArchivalStatusBatchRequestV2.MaxFiles} entries");
            OdinValidationUtils.AssertIsTrue(request.Files.All(f => f.FileId != Guid.Empty && f.VersionTag != Guid.Empty),
                "every file needs a fileId and a versionTag");
            OdinValidationUtils.AssertIsTrue(request.Files.Select(f => f.FileId).Distinct().Count() == request.Files.Count,
                "a file may appear only once");

            WebOdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);

            var storage = GetFileSystem().Storage;
            var results = new List<UpdateArchivalStatusResultV2>(request.Files.Count);
            foreach (var f in request.Files)
            {
                try
                {
                    var newVersionTag = await storage.UpdateArchivalStatus(new InternalDriveFileId(driveId, f.FileId), f.VersionTag,
                        request.ArchivalStatus, WebOdinContext);
                    results.Add(new UpdateArchivalStatusResultV2 { FileId = f.FileId, NewVersionTag = newVersionTag });
                }
                catch (OdinClientException e) when (e.ErrorCode is OdinClientErrorCode.VersionTagMismatch
                                                        or OdinClientErrorCode.FileNotFound
                                                        or OdinClientErrorCode.CannotUpdateNonActiveFile)
                {
                    results.Add(new UpdateArchivalStatusResultV2 { FileId = f.FileId, ErrorCode = e.ErrorCode });
                }
            }

            return new UpdateArchivalStatusBatchResultV2 { Results = results };
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
                    GroupId = new List<Guid>() { groupIdToBeDeleted },
                    FileState = new List<FileState>() { FileState.Active }
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

            Logger.LogDebug("[DeleteFlow] DeleteFileIdBatch entry -> tenant:{tenant} caller:{caller} driveId:{driveId} requestCount:{count}",
                WebOdinContext.Tenant,
                WebOdinContext.Caller?.OdinId,
                driveId,
                request?.Requests?.Count ?? 0);

            var batchResult = new DeleteFileIdBatchResultV2()
            {
                Results = await DeleteFileIdBatchInternal(driveId, request.Requests)
            };

            Logger.LogDebug("[DeleteFlow] DeleteFileIdBatch exit -> driveId:{driveId} resultCount:{count}",
                driveId, batchResult.Results.Count);

            return batchResult;
        }
    }
}