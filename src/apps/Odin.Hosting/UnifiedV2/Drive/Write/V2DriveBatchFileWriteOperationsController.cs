using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
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
        /// Sets only the archival status of up to <see cref="FileVersionTagBatchV2.MaxFiles"/> files.
        /// </summary>
        [HttpPost("update-archival-status-batch")]
        [SwaggerOperation(
            Summary = "Set the archival status of many files",
            Description = "Sets appData.archivalStatus on each file and leaves the rest of its header as it is, so " +
                          "the caller needs neither the key header nor the content. Each file is updated only if its " +
                          "version tag still matches. Results are per file; see UpdateArchivalStatusResultV2.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        public async Task<UpdateArchivalStatusBatchResultV2> UpdateArchivalStatusBatch(Guid driveId,
            [FromBody] UpdateArchivalStatusBatchRequestV2 request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            FileVersionTagBatchV2.AssertValid(request.Files);
            OdinValidationUtils.AssertIsTrue(request.ArchivalStatus.HasValue, "archivalStatus is required");

            WebOdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);

            var status = request.ArchivalStatus!.Value;
            var storage = GetFileSystem().Storage;
            var results = await PerFileAsync(driveId, request.Files,
                async (file, f) =>
                {
                    var (versionTag, updated) = await storage.UpdateArchivalStatus(file, f.VersionTag, status, WebOdinContext);
                    return new UpdateArchivalStatusResultV2 { FileId = f.FileId, NewVersionTag = versionTag, Updated = updated };
                },
                (fileId, errorCode) => new UpdateArchivalStatusResultV2 { FileId = fileId, ErrorCode = errorCode });

            return new UpdateArchivalStatusBatchResultV2 { Results = results };
        }

        /// <summary>
        /// Hard-deletes up to <see cref="FileVersionTagBatchV2.MaxFiles"/> files, each only at its version tag.
        /// </summary>
        [HttpPost("hard-delete-batch")]
        [SwaggerOperation(
            Summary = "Hard delete many files, each at a known version",
            Description = "Permanently removes each file (active or a tombstone) with its payloads and thumbnails, only if its " +
                          "version tag still matches. Nothing is left in the change feed, and nothing is sent to peers. " +
                          "Results are per file; see HardDeleteResultV2.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        public async Task<HardDeleteBatchResultV2> HardDeleteBatch(Guid driveId, [FromBody] HardDeleteBatchRequestV2 request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            FileVersionTagBatchV2.AssertValid(request.Files);

            WebOdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);

            var storage = GetFileSystem().Storage;
            var results = await PerFileAsync(driveId, request.Files,
                async (file, f) =>
                {
                    await storage.HardDeleteLongTermFile(file, f.VersionTag, WebOdinContext);
                    return new HardDeleteResultV2 { FileId = f.FileId };
                },
                (fileId, errorCode) => new HardDeleteResultV2 { FileId = fileId, ErrorCode = errorCode });

            // Nothing else records an irreversible delete: no feed row, nothing to peers
            Logger.LogInformation(
                "[DeleteFlow] HardDeleteBatch tenant:{tenant} caller:{caller} driveId:{driveId} requested:{requested} deleted:{deleted} files:{files}",
                WebOdinContext.Tenant, WebOdinContext.Caller?.OdinId, driveId, results.Count, results.Count(r => r.ErrorCode == null),
                string.Join(",", results.Where(r => r.ErrorCode == null).Select(r => r.FileId)));

            return new HardDeleteBatchResultV2 { Results = results };
        }

        /// <summary>
        /// Soft-deletes up to <see cref="FileVersionTagBatchV2.MaxFiles"/> files, each optionally only at its version tag,
        /// and tells each file's recipients to delete their copies.
        /// </summary>
        [HttpPost("soft-delete-batch")]
        [SwaggerOperation(
            Summary = "Delete many files, leaving tombstones",
            Description = "Deletes each file, leaving a tombstone in the change feed, only if its version tag still matches " +
                          "when one is given (a file already deleted is not checked), and tells the file's recipients to " +
                          "delete their copies. A malformed batch, or a caller that may not reach peers, is refused whole " +
                          "before anything is touched. Results are per file; see SoftDeleteResultV2.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        public async Task<SoftDeleteBatchResultV2> SoftDeleteBatch(Guid driveId, [FromBody] SoftDeleteBatchRequestV2 request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            request.AssertValid(WebOdinContext.Tenant);

            WebOdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
            // Every recipient once, up front: a caller that may not reach peers is refused before any file is touched
            var tokens = await PeerOutgoingTransferService.ResolveRecipientTokensAsync(
                request.Files.SelectMany(f => f.Recipients ?? []).ToOdinIdList(), WebOdinContext);

            var results = await PerFileAsync(driveId, request.Files,
                (file, f) => SoftDeleteFileAsync(file, f.VersionTag, f.Recipients?.ToOdinIdList() ?? [], tokens),
                (fileId, errorCode) => new SoftDeleteResultV2 { FileId = fileId, ErrorCode = errorCode });

            return new SoftDeleteBatchResultV2 { Results = results };
        }

        /// <summary>
        /// Runs <paramref name="write"/> on each file in request order. A client error becomes that file's result, so one
        /// bad file never stops the others nor strands the ones already written before it; anything else fails the request.
        /// </summary>
        private static async Task<List<TResult>> PerFileAsync<TFile, TResult>(Guid driveId, List<TFile> files,
            Func<InternalDriveFileId, TFile, Task<TResult>> write, Func<Guid, OdinClientErrorCode, TResult> failed)
            where TFile : IBatchFileV2
        {
            var results = new List<TResult>(files.Count);
            foreach (var f in files)
            {
                try
                {
                    results.Add(await write(new InternalDriveFileId(driveId, f.FileId), f));
                }
                catch (OdinClientException e)
                {
                    results.Add(failed(f.FileId, e.ErrorCode));
                }
            }

            return results;
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