using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Drives;
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

                try
                {
                    results.Add(await PerformFileDelete(driveId, r.FileId, options));
                }
                catch (OdinClientException e) when (e.ErrorCode == OdinClientErrorCode.VersionTagMismatch)
                {
                    // A stale tag answers for its own file; the rest of the batch is still deleted. Only this error,
                    // unlike PerFileAsync: a batch without tags still fails whole on any other client error, as before
                    results.Add(new DeleteFileResultV2
                    {
                        FileId = r.FileId,
                        RecipientStatus = new Dictionary<string, DeleteLinkedFileStatus>(),
                        ErrorCode = e.ErrorCode
                    });
                }
            }

            logger.LogDebug("[DeleteFlow] DeleteFileIdBatchInternal -> driveId:{driveId} complete; localDeleted:{ld} notFound:{nf}",
                driveId,
                results.Count(x => x.LocalFileDeleted),
                results.Count(x => x.LocalFileNotFound));

            return results;
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