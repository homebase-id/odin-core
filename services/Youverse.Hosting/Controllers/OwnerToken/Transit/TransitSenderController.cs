using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.TransitSenderV1)]
    [AuthorizeValidOwnerToken]
    public class TransitSenderController : DriveUploadControllerBase
    {
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
                throw new YouverseClientException("Data is not multi-part content", YouverseClientErrorCode.MissingUploadData);
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
            var uploadInstructionSet = await MapTransitInstructionSet(section!.Body);

            var packageId = await driveUploadService.CreatePackage(uploadInstructionSet);

            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Metadata);
            await driveUploadService.AddMetadata(packageId, section!.Body);

            //
            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Payload);
            await driveUploadService.AddPayload(packageId, section!.Body);

            //
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                AssertIsValidThumbnailPart(section, MultipartUploadParts.Thumbnail, out var fileSection, out var width, out var height);
                await driveUploadService.AddThumbnail(packageId, width, height, fileSection.Section.ContentType, fileSection.FileStream);
                section = await reader.ReadNextSectionAsync();
            }

            var uploadResult = await driveUploadService.FinalizeUpload(packageId);

            //TODO: this should come from the transit system
            // We need to return the remote information instead of the local drive information
            return new TransitResult()
            {
                RemoteGlobalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier()
                {
                    GlobalTransitId =  uploadResult.GlobalTransitId.GetValueOrDefault(),
                    TargetDrive  = uploadInstructionSet.TransitOptions.RemoteTargetDrive
                },
                RecipientStatus = uploadResult.RecipientStatus
            };
        }

        /*
        /// <summary>
        /// Deletes a file
        /// </summary>
        /// <param name="request"></param>
        */
        // [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        // [HttpPost("files/delete")]
        // public async Task<IActionResult> DeleteFile([FromBody] DeleteFileByGlobalTransitIdRequest request)
        // {
        //     //TODO: send the delete request for request.File
        //
        //     // var driveId = DotYouContext.PermissionsContext.GetDriveId(request.File.TargetDrive);
        //     //
        //     // var file = new InternalDriveFileId()
        //     // {
        //     //     DriveId = driveId,
        //     //     FileId = request.File.FileId
        //     // };
        //     //
        //     // var result = await _appService.DeleteFile(file, request.Recipients);
        //     // if (result.LocalFileNotFound)
        //     // {
        //     //     return NotFound();
        //     // }
        //
        //     var result = "";
        //     return new JsonResult(result);
        // }


        /// <summary>
        /// Map the client's transit instructions to an upload instruction set so we
        /// so we can keep the upload infrastructure for Alpha
        /// </summary>
        private async Task<UploadInstructionSet> MapTransitInstructionSet(Stream transitInstructionStream)
        {
            string json = await new StreamReader(transitInstructionStream).ReadToEndAsync();
            var transitInstructionSet = DotYouSystemSerializer.Deserialize<TransitInstructionSet>(json);

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

    public class DeleteFileByGlobalTransitIdRequest
    {
        /// <summary>
        /// The file to be deleted
        /// </summary>
        public GlobalTransitIdFileIdentifier File { get; set; }

        /// <summary>
        /// List of recipients to receive the delete-file notification
        /// </summary>
        public List<string> Recipients { get; set; }
    }
}