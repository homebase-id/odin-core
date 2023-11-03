using System;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.OwnerToken;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Base.Transit
{
    /// <summary />
    public class TransitSenderControllerBase : DriveUploadControllerBase
    {
        private readonly ITransitService _transitService;

        public TransitSenderControllerBase(ITransitService transitService)
        {
            _transitService = transitService;
        }

        /// <summary>
        /// Uploads a file using multi-part form data
        /// </summary>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/send")]
        public async Task<TransitResult> SendFile()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new OdinClientException("Data is not multi-part content", OdinClientErrorCode.MissingUploadData);
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var driveUploadService = this.GetFileSystemResolver().ResolveFileSystemWriter();

            // Note: comparing this to a drive upload - 
            // We receive TransitInstructionSet from the client then
            // map it to a UploadInstructionSet using a hard-coded internal
            // drive to allow apps to send files directly

            // Post alpha we can consider a more direct operation w/ot he temp-transient-drive

            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Instructions);

            //
            // re-map to transit instruction set.  this is the critical point of this feature
            //
            var uploadInstructionSet = await MapTransitInstructionSet(section!.Body);

            await driveUploadService.StartUpload(uploadInstructionSet);

            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Metadata);
            await driveUploadService.AddMetadata(section!.Body);

            //
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                if (IsPayloadPart(section))
                {
                    AssertIsPayloadPart(section, out var fileSection, out var payloadKey, out var contentType);
                    await driveUploadService.AddPayload(payloadKey, contentType, fileSection.FileStream);
                }

                if (IsThumbnail(section))
                {
                    AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey, out var contentType);
                    await driveUploadService.AddThumbnail(thumbnailUploadKey, contentType, fileSection.FileStream);
                }

                section = await reader.ReadNextSectionAsync();
            }

            var uploadResult = await driveUploadService.FinalizeUpload();

            //TODO: this should come from the transit system
            // We need to return the remote information instead of the local drive information
            return new TransitResult()
            {
                RemoteGlobalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier()
                {
                    GlobalTransitId = uploadResult.GlobalTransitId.GetValueOrDefault(),
                    TargetDrive = uploadInstructionSet.TransitOptions.RemoteTargetDrive
                },
                RecipientStatus = uploadResult.RecipientStatus
            };
        }

        /// <summary>
        /// Sends a Delete Linked File Request to recipients
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/senddeleterequest")]
        public async Task<IActionResult> DeleteFile([FromBody] DeleteFileByGlobalTransitIdRequest request)
        {
            Guard.Argument(request, nameof(request)).NotNull();
            Guard.Argument(request.Recipients, nameof(request.Recipients)).NotEmpty();
            Guard.Argument(request.GlobalTransitIdFileIdentifier, nameof(request.GlobalTransitIdFileIdentifier))
                .Require(g => g.TargetDrive.IsValid())
                .Require(g => g.GlobalTransitId != Guid.Empty);

            //TODO: send the delete request for request.File

            //send the deleted file
            var map = await _transitService.SendDeleteLinkedFileRequest(request.GlobalTransitIdFileIdentifier,
                new SendFileOptions()
                {
                    FileSystemType = request.FileSystemType,
                    TransferFileType = TransferFileType.Normal
                },
                request.Recipients);

            return new JsonResult(map);
        }


        /// <summary>
        /// Map the client's transit instructions to an upload instruction set so we
        /// so we can keep the upload infrastructure for Alpha
        /// </summary>
        private async Task<UploadInstructionSet> MapTransitInstructionSet(Stream transitInstructionStream)
        {
            string json = await new StreamReader(transitInstructionStream).ReadToEndAsync();
            var transitInstructionSet = OdinSystemSerializer.Deserialize<TransitInstructionSet>(json);

            var uploadInstructionSet = new UploadInstructionSet()
            {
                TransferIv = transitInstructionSet.TransferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = SystemDriveConstants.TransientTempDrive,
                    OverwriteFileId = default
                },
                TransitOptions = new TransitOptions()
                {
                    IsTransient = true,
                    UseGlobalTransitId = true,
                    SendContents = SendContents.All,

                    //TODO: OMG HACK
                    OverrideRemoteGlobalTransitId = transitInstructionSet.GlobalTransitFileId,

                    RemoteTargetDrive = transitInstructionSet.RemoteTargetDrive,
                    Recipients = transitInstructionSet.Recipients,
                    Schedule = transitInstructionSet.Schedule
                }
            };

            return uploadInstructionSet;
        }
    }
}