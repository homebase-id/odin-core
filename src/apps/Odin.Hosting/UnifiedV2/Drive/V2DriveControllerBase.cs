using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.Drive
{
    public class V2DriveControllerBase(PeerOutgoingTransferService peerOutgoingTransferService) : OdinControllerBase
    {
        protected PeerOutgoingTransferService PeerOutgoingTransferService => peerOutgoingTransferService;

        protected async Task<DeleteFileResultV2> PerformFileDelete(Guid driveId, Guid fileId, DeleteFileOptionsV2 options)
        {
            var recipients = options?.Recipients ?? [];

            OdinValidationUtils.AssertValidRecipientList(recipients, allowEmpty: true);

            var file = new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = driveId
            };

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
                result.LocalFileNotFound = true;
                return result;
            }

            if (recipients.Any())
            {
                //send the deleted file
                var responses = await peerOutgoingTransferService.SendDeleteFileRequest(file,
                    new FileTransferOptions()
                    {
                        FileSystemType = header.ServerMetadata.FileSystemType,
                        TransferFileType = TransferFileType.Normal
                    },
                    recipients, WebOdinContext);

                result.RecipientStatus = responses;
            }

            await fs.Storage.SoftDeleteLongTermFile(file, WebOdinContext, null);
            result.LocalFileDeleted = true;
            return result;
        }

        protected async Task<List<DeleteFileResultV2>> DeleteFileIdBatchInternal(Guid driveId, List<DeleteFileRequestV2> requests)
        {
            WebOdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);

            var results = new List<DeleteFileResultV2>();
            foreach (var r in requests)
            {
                var options = new DeleteFileOptionsV2()
                {
                    Recipients = r.Recipients ?? []
                };

                var result = await PerformFileDelete(driveId, r.FileId, options);
                results.Add(result);
            }

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