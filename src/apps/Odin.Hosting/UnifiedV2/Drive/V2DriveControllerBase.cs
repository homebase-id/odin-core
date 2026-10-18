using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.Drive
{
    public class V2DriveControllerBase(
        PeerOutgoingTransferService peerOutgoingTransferService,
        ILogger<V2DriveControllerBase> logger) : OdinControllerBase
    {
        protected PeerOutgoingTransferService PeerOutgoingTransferService => peerOutgoingTransferService;
        protected ILogger<V2DriveControllerBase> Logger => logger;

        protected async Task<DeleteFileResultV2> PerformFileDelete(Guid driveId, Guid fileId, DeleteFileOptionsV2 options)
        {
            var recipients = options?.Recipients ?? [];

            OdinValidationUtils.AssertValidRecipientList(recipients, allowEmpty: true);

            var file = new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = driveId
            };

            logger.LogDebug("[DeleteFlow] PerformFileDelete -> tenant:{tenant} driveId:{driveId} fileId:{fileId} recipientCount:{count} recipients:[{recipients}]",
                WebOdinContext.Tenant, driveId, fileId, recipients.Count, string.Join(",", recipients));

            var result = new DeleteFileResultV2()
            {
                FileId = fileId,
                RecipientStatus = new Dictionary<string, DeleteLinkedFileStatus>(),
                LocalFileDeleted = false
            };

            var fs = this.GetFileSystem();
            var header = await fs.Storage.GetServerFileHeaderForWriting(file, WebOdinContext);
            if (header == null)
            {
                logger.LogDebug("[DeleteFlow] PerformFileDelete -> local header not found for fileId:{fileId} on driveId:{driveId}; skipping",
                    fileId, driveId);
                result.LocalFileNotFound = true;
                return result;
            }

            // Before recipients are told: a delete refused here must not reach them
            DriveStorageServiceBase.AssertVersionTagMatches(header, options?.VersionTag);

            if (recipients.Any())
            {
                logger.LogDebug("[DeleteFlow] PerformFileDelete -> dispatching peer delete for fileId:{fileId} gtid:{gtid} fileSystemType:{fst} to {count} recipient(s)",
                    fileId, header.FileMetadata.GlobalTransitId, header.ServerMetadata.FileSystemType, recipients.Count);

                //send the deleted file
                var responses = await peerOutgoingTransferService.SendDeleteFileRequest(file,
                    new FileTransferOptions()
                    {
                        FileSystemType = header.ServerMetadata.FileSystemType,
                        TransferFileType = TransferFileType.Normal
                    },
                    recipients, WebOdinContext);

                result.RecipientStatus = responses;

                logger.LogDebug("[DeleteFlow] PerformFileDelete -> peer enqueue results for fileId:{fileId}: [{statuses}]",
                    fileId, string.Join(",", responses.Select(kv => $"{kv.Key}={kv.Value}")));
            }

            result.Updated = await fs.Storage.SoftDeleteLongTermFile(file, WebOdinContext, null, options?.VersionTag);
            result.LocalFileDeleted = result.Updated != null;

            logger.LogDebug("[DeleteFlow] PerformFileDelete -> local soft-delete complete for fileId:{fileId} on driveId:{driveId}",
                fileId, driveId);

            return result;
        }

        protected async Task<List<DeleteFileResultV2>> DeleteFileIdBatchInternal(Guid driveId, List<DeleteFileRequestV2> requests)
        {
            WebOdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);

            logger.LogDebug("[DeleteFlow] DeleteFileIdBatchInternal -> driveId:{driveId} batchCount:{count}",
                driveId, requests?.Count ?? 0);

            // Before the first file, so a refusal touches nothing
            if (requests.Any(r => r.Recipients?.Count > 0))
            {
                PeerOutgoingTransferService.AssertCanTellRecipientsToDelete(GetFileSystemType(), WebOdinContext);
            }

            // Check every version tag before deleting anything, so a stale tag fails the batch whole
            // instead of after the files ahead of it were deleted
            var fs = this.GetFileSystem();
            foreach (var r in requests.Where(r => r.VersionTag.HasValue))
            {
                var header = await fs.Storage.GetServerFileHeaderForWriting(new InternalDriveFileId(driveId, r.FileId), WebOdinContext);
                if (header != null)
                {
                    DriveStorageServiceBase.AssertVersionTagMatches(header, r.VersionTag);
                }
            }

            var results = new List<DeleteFileResultV2>();
            foreach (var r in requests)
            {
                var options = new DeleteFileOptionsV2()
                {
                    Recipients = r.Recipients ?? [],
                    VersionTag = r.VersionTag
                };

                logger.LogDebug("[DeleteFlow] DeleteFileIdBatchInternal -> processing fileId:{fileId} recipientCount:{rc}",
                    r.FileId, options.Recipients.Count);

                var result = await PerformFileDelete(driveId, r.FileId, options);
                results.Add(result);
            }

            logger.LogDebug("[DeleteFlow] DeleteFileIdBatchInternal -> driveId:{driveId} complete; localDeleted:{ld} notFound:{nf}",
                driveId,
                results.Count(x => x.LocalFileDeleted),
                results.Count(x => x.LocalFileNotFound));

            return results;
        }

        /// <summary>
        /// Soft-deletes one file of a batch, telling its recipients first with the batch's pre-resolved tokens. Throws a
        /// client error only before anything is sent or written; after that, a client failure is in the result, beside
        /// who was told.
        /// </summary>
        protected async Task<SoftDeleteResultV2> SoftDeleteFileAsync(InternalDriveFileId file, Guid? versionTag,
            List<OdinId> recipients, IReadOnlyDictionary<OdinId, byte[]> recipientTokens)
        {
            var storage = GetFileSystem().Storage;
            var header = await storage.GetServerFileHeaderForWriting(file, WebOdinContext, aclDeniedAsMissing: true, includeExpired: true)
                         ?? throw new OdinClientException("Cannot delete a file that does not exist", OdinClientErrorCode.FileNotFound);

            // A tombstone is done (a retry whose answer was lost, or a delete-for-me then for-everyone): its tag has moved
            // on, so it is not checked, but its recipients are still told
            var alreadyDeleted = header.FileMetadata.FileState == FileState.Deleted;
            if (!alreadyDeleted)
            {
                DriveStorageServiceBase.AssertVersionTagMatches(header, versionTag);
            }

            // Recipients before the local delete: if the delete then fails, the copy stays to retry from, which the
            // reverse would not leave
            var recipientStatus = recipients.Count == 0 ? []
                : await peerOutgoingTransferService.SendDeleteFileRequest(header, recipients, recipientTokens, WebOdinContext);

            UnixTimeUtc? updated = header.FileMetadata.Updated;
            OdinClientErrorCode? errorCode = null;
            if (!alreadyDeleted)
            {
                try
                {
                    // Null is a failure the tombstone write logged and swallowed; like hard delete, it fails the request
                    updated = await storage.SoftDeleteLongTermFile(file, WebOdinContext, null, versionTag)
                              ?? throw new OdinSystemException($"Soft delete of {file} failed");
                }
                catch (OdinClientException e)
                {
                    // An edit since the check above; the recipients were told already, and the result says so
                    (updated, errorCode) = (null, e.ErrorCode);
                }
            }

            return new SoftDeleteResultV2 { FileId = file.FileId, Updated = updated, ErrorCode = errorCode, RecipientStatus = recipientStatus };
        }

        protected IDriveFileSystem GetFileSystem()
        {
            return GetHttpFileSystemResolver().ResolveFileSystem();
        }

        protected FileSystemType GetFileSystemType()
        {
            return GetHttpFileSystemResolver().GetFileSystemType();
        }
    }
}