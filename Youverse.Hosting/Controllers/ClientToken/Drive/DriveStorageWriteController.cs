using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [AuthorizeValidExchangeGrant]
    public class DriveStorageWriteController : ControllerBase
    {
        private readonly ITransitService _transitService;
        private readonly IMultipartPackageStorageWriter _packageStorageWriter;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveService _driveService;
        public DriveStorageWriteController(IMultipartPackageStorageWriter packageStorageWriter, ITransitService transitService, DotYouContextAccessor contextAccessor, IDriveService driveService)
        {
            _packageStorageWriter = packageStorageWriter;
            _transitService = transitService;
            _contextAccessor = contextAccessor;
            _driveService = driveService;
        }
        
        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpDelete("files")]
        public async Task DeleteFile([FromQuery] TargetDrive drive, [FromQuery] Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive),
                FileId = fileId
            };
            await _driveService.DeleteLongTermFile(file);
        }
        
        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("files/upload")]
        public async Task<IActionResult> Upload()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new UploadException("Data is not multi-part content");
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Instructions);
            var packageId = await _packageStorageWriter.CreatePackage(section!.Body);
            
            //
            
            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Metadata);
            await _packageStorageWriter.AddMetadata(packageId, section!.Body);
            
            //
            
            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Payload);
            await _packageStorageWriter.AddPayload(packageId, section!.Body);
            
            //
            
            var package = await _packageStorageWriter.GetPackage(packageId);
            var status = await _transitService.AcceptUpload(package);
            return new JsonResult(status);
        }

        private void AssertIsPart(MultipartSection section, MultipartUploadParts expectedPart)
        {
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new UploadException($"Part must be {Enum.GetName(expectedPart)}");
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