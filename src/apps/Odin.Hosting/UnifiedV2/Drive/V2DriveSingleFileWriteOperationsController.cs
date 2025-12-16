using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Update;
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
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveSingleFileWriteOperationsController(PeerOutgoingTransferService peerOutgoingTransferService)
        : V2DriveControllerBase(peerOutgoingTransferService)
    {
        [SwaggerOperation(
            Summary = "Update local metadata tags",
            Description = "Updates tag metadata for a specified file in local storage.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [HttpPatch("update-local-metadata-tags")]
        public async Task<UpdateLocalMetadataResultV2> UpdateLocalMetadataTags(Guid driveId, Guid fileId,
            [FromBody] UpdateLocalMetadataTagsRequestV2 request)
        {
            OdinValidationUtils.AssertNotEmptyGuid(fileId, "file id is invalid");

            var fs = this.GetFileSystem();
            var v1 = await fs.Storage.UpdateLocalMetadataTags(
                new InternalDriveFileId(driveId, fileId),
                request.LocalVersionTag,
                request.Tags,
                WebOdinContext);

            var v2 = new UpdateLocalMetadataResultV2()
            {
                NewLocalVersionTag = v1.NewLocalVersionTag
            };

            return v2;
        }

        [SwaggerOperation(
            Summary = "Update local metadata content",
            Description = "Updates internal local metadata content sections for a file.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [HttpPatch("update-local-metadata-content")]
        public async Task<UpdateLocalMetadataResult> UpdateLocalMetadataContent(Guid driveId, Guid fileId,
            [FromBody] UpdateLocalMetadataContentRequestV2 request)
        {
            OdinValidationUtils.AssertNotEmptyGuid(fileId, "File is invalid");

            var fs = this.GetFileSystem();
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
        public async Task<DeleteFileResultV2> DeleteFile(Guid driveId, Guid fileId, [FromBody] DeleteFileOptionsV2 options)
        {
            var result = await PerformFileDelete(driveId, fileId, options);
            return result;
        }

        [SwaggerOperation(
            Summary = "Delete file payload",
            Description = "Deletes a payload associated with a file without removing the file entry itself.",
            Tags = [SwaggerInfo.FileWrite]
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
            var fs = this.GetFileSystem();

            return new DeletePayloadResult()
            {
                NewVersionTag = await fs.Storage.DeletePayload(file, request.Key, request.VersionTag.GetValueOrDefault(), WebOdinContext)
            };
        }

        [SwaggerOperation(
            Summary = "Hard delete a file",
            Description = "Permanently removes a file and all associated metadata. Irreversible.",
            Tags = [SwaggerInfo.FileWrite]
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

            var fs = this.GetFileSystem();
            await fs.Storage.HardDeleteLongTermFile(file, WebOdinContext);
            return Ok();
        }
    }
}