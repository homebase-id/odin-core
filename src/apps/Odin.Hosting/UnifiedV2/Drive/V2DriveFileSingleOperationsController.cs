using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive
{
    /// <summary>
    /// File operations for the Unified V2 Drive API.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.ByFileId)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveFileSingleOperationsController(PeerOutgoingTransferService peerOutgoingTransferService)
        : OdinControllerBase
    {
        [SwaggerOperation(
            Summary = "Update local metadata tags",
            Description = "Updates tag metadata for a specified file in local storage.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [HttpPatch("update-local-metadata-tags")]
        public async Task<UpdateLocalMetadataResult> UpdateLocalMetadataTags(Guid driveId, Guid fileId,
            [FromBody] UpdateLocalMetadataTagsRequest request)
        {
            OdinValidationUtils.AssertIsTrue(request.File.HasValue(), "File is invalid");

            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();
            return await fs.Storage.UpdateLocalMetadataTags(
                new InternalDriveFileId(driveId, fileId),
                request.LocalVersionTag,
                request.Tags,
                WebOdinContext);
        }

        [SwaggerOperation(
            Summary = "Update local metadata content",
            Description = "Updates internal local metadata content sections for a file.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [HttpPatch("update-local-metadata-content")]
        public async Task<UpdateLocalMetadataResult> UpdateLocalMetadataContent(Guid driveId, Guid fileId,
            [FromBody] UpdateLocalMetadataContentRequest request)
        {
            OdinValidationUtils.AssertIsTrue(request.File.HasValue(), "File is invalid");

            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();
            return await fs.Storage.UpdateLocalMetadataContent(
                new InternalDriveFileId(driveId, fileId),
                request.LocalVersionTag,
                request.Iv,
                request.Content,
                WebOdinContext);
        }

        [SwaggerOperation(
            Summary = "Delete a file",
            Description = "Deletes a file using a POST request for maximum compatibility.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [HttpPost("delete")]
        public async Task<IActionResult> DeleteFile(Guid driveId, Guid fileId, [FromBody] DeleteFileOptionsV2 options)
        {
            var result = await PerformFileDeleteInternal(driveId, fileId, options);
            return Ok(result);
        }

        [SwaggerOperation(
            Summary = "Delete file payload",
            Description = "Deletes a payload associated with a file without removing the file entry itself.",
            Tags = [SwaggerInfo.FileDelete]
        )]
        [HttpPost("delete-payload")]
        public async Task<DeletePayloadResult> DeletePayload(Guid driveId, Guid fileId, DeletePayloadRequestV2 request)
        {
            TenantPathManager.AssertValidPayloadKey(request.Key);
            if (request.VersionTag == null)
            {
                throw new OdinClientException("Missing version tag", OdinClientErrorCode.MissingVersionTag);
            }

            var file = new InternalDriveFileId(driveId, fileId);
            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem(request.FileSystemType);

            return new DeletePayloadResult()
            {
                NewVersionTag = await fs.Storage.DeletePayload(file, request.Key, request.VersionTag.GetValueOrDefault(), WebOdinContext)
            };
        }

        [SwaggerOperation(
            Summary = "Hard delete a file",
            Description = "Permanently removes a file and all associated metadata. Irreversible.",
            Tags = [SwaggerInfo.FileDelete]
        )]
        [HttpPost("hard-delete")]
        public async Task<IActionResult> HardDeleteFile(Guid driveId, Guid fileId, [FromBody] DeleteFileOptionsV2 options)
        {
            if (options.Recipients?.Any() ?? false)
            {
                throw new OdinClientException("Cannot specify recipients when hard-deleting a file", OdinClientErrorCode.InvalidRecipient);
            }

            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = fileId
            };

            await GetHttpFileSystemResolver().ResolveFileSystem().Storage.HardDeleteLongTermFile(file, WebOdinContext);
            return Ok();
        }

        private async Task<DeleteFileResultV2> PerformFileDeleteInternal(Guid driveId, Guid fileId, DeleteFileOptionsV2 options)
        {
            var recipients = options.Recipients;

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

            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem(options.FileSystemType);
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
    }
}