using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [AuthorizeValidExchangeGrant]
    public class DriveStorageWriteController : ControllerBase
    {
        private readonly DriveUploadService _driveUploadService;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IAppService _appService;

        public DriveStorageWriteController(DriveUploadService driveUploadService, DotYouContextAccessor contextAccessor, IDriveService driveService, ITransitService transitService,
            IAppService appService)
        {
            _driveUploadService = driveUploadService;
            _contextAccessor = contextAccessor;
            _appService = appService;
        }

        /// <summary>
        /// Deletes a file
        /// </summary>
        /// <param name="request"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/delete")]
        public async Task<IActionResult> DeleteFile([FromBody] DeleteFileRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.File.TargetDrive);

            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = request.File.FileId
            };

            var result = await _appService.DeleteFile(file, request.Recipients);
            if (result.LocalFileNotFound)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        /// <summary>
        /// Uploads a file using multi-part form data
        /// </summary>
        /// <returns></returns>
        /// <exception cref="UploadException"></exception>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/upload")]
        public async Task<UploadResult> Upload()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new YouverseClientException("Data is not multi-part content");
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Instructions);
            var packageId = await _driveUploadService.CreatePackage(section!.Body);

            //

            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Metadata);
            await _driveUploadService.AddMetadata(packageId, section!.Body);

            //

            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Payload);
            await _driveUploadService.AddPayload(packageId, section!.Body);

            //

            //The next section must be thumbnail
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                AssertIsValidThumbnailPart(section, MultipartUploadParts.Thumbnail, out var fileSection, out var width, out var height);
                await _driveUploadService.AddThumbnail(packageId, width, height, fileSection.Section.ContentType, fileSection.FileStream);
                section = await reader.ReadNextSectionAsync();
            }

            //

            var status = await _driveUploadService.FinalizeUpload(packageId);
            return status;
        }

        private void AssertIsPart(MultipartSection section, MultipartUploadParts expectedPart)
        {
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new YouverseClientException($"Part must be {Enum.GetName(expectedPart)}");
            }
        }

        private void AssertIsValidThumbnailPart(MultipartSection section, MultipartUploadParts expectedPart, out FileMultipartSection fileSection, out int width, out int height)
        {
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new YouverseClientException($"Thumbnails have name of {Enum.GetName(expectedPart)}");
            }

            fileSection = section.AsFileSection();
            if (null == fileSection)
            {
                throw new YouverseClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')");
            }

            string[] parts = fileSection.FileName.Split('x');
            if (!Int32.TryParse(parts[0], out width) || !Int32.TryParse(parts[1], out height))
            {
                throw new YouverseClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')");
            }
        }

        private static bool IsMultipartContentType(string contentType)
        {
            return !string.IsNullOrEmpty(contentType) && contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetBoundary(string contentType)
        {
            var elements = contentType.Split(' ');
            var element = elements.First(entry => entry.StartsWith("boundary="));
            var boundary = element.Substring("boundary=".Length);
            // Remove quotes
            if (boundary.Length >= 2 && boundary[0] == '"' &&
                boundary[^1] == '"')
            {
                boundary = boundary.Substring(1, boundary.Length - 2);
            }

            return boundary;
        }

        private string GetSectionName(string contentDisposition)
        {
            var cd = ContentDispositionHeaderValue.Parse(contentDisposition);
            return cd.Name?.Trim('"');
        }
    }
}