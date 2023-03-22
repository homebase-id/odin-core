using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveStorageV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerCommentStorageController : DriveUploadControllerBase
    {
        private readonly IAppService _appService;

        /// <summary />
        public OwnerCommentStorageController(IAppService appService)
        {
            _appService = appService;
        }

        /// <summary>
        /// Uploads a file using multi-part form data
        /// </summary>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("comments/files/upload")]
        public async Task<UploadResult> Upload()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new YouverseClientException("Data is not multi-part content", YouverseClientErrorCode.MissingUploadData);
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var driveUploadService = this.GetFileSystemResolver().ResolveFileSystemWriter();

            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Instructions);
            var packageId = await driveUploadService.CreatePackage(section!.Body);

            //
            // hack some changes on the package specific to comments
            //

            var optionsFromStef = new TransitExpressOptions()
            {
                OverwriteGlobalTransitFileId = default, //
                Schedule = ScheduleOptions.SendLater,
                Recipients = default,
            };

            var pkg = await driveUploadService.GetPackage(packageId);
           
            //remap accordingly
            pkg.InstructionSet.StorageOptions.Drive = SystemDriveConstants.TransientTempDrive;
            pkg.InstructionSet.TransitOptions.IsTransient = true;
            pkg.InstructionSet.TransitOptions.UseGlobalTransitId = true;
            pkg.InstructionSet.TransitOptions.SendContents = SendContents.All;

            //take options from the caller
            pkg.InstructionSet.TransitOptions.OverrideTargetDrive = optionsFromStef.OverwriteGlobalTransitFileId!.TargetDrive;
            pkg.InstructionSet.TransitOptions.Recipients = optionsFromStef.Recipients;
            pkg.InstructionSet.TransitOptions.Schedule = optionsFromStef.Schedule;

            driveUploadService.UpdatePackageHack(pkg);

            //
            // end hack
            //

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

            var status = await driveUploadService.FinalizeUpload(packageId);
            return status;
        }

        /// <summary>
        /// Deletes a file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("comments/files/delete")]
        public async Task<IActionResult> DeleteFile([FromBody] DeleteFileByGlobalTransitIdRequest request)
        {

            //TODO: send the delete request for request.File

            // var driveId = DotYouContext.PermissionsContext.GetDriveId(request.File.TargetDrive);
            //
            // var file = new InternalDriveFileId()
            // {
            //     DriveId = driveId,
            //     FileId = request.File.FileId
            // };
            //
            // var result = await _appService.DeleteFile(file, request.Recipients);
            // if (result.LocalFileNotFound)
            // {
            //     return NotFound();
            // }

            var result = "";
            return new JsonResult(result);
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