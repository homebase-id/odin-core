using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.ClientToken.Drive;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveStorageV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveStorageController : ControllerBase
    {
        private readonly IAppService _appService;
        private readonly IDriveService _driveService;
        private readonly DotYouContextAccessor _contextAccessor;

        public OwnerDriveStorageController(DotYouContextAccessor contextAccessor, IDriveService driveService, IAppService appService)
        {
            _contextAccessor = contextAccessor;
            _driveService = driveService;
            _appService = appService;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("header")]
        public async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };
            var result = await _appService.GetClientEncryptedFileHeader(file);
            return new JsonResult(result);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("payload")]
        public async Task<IActionResult> GetPayloadStream([FromBody] ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };

            var payload = await _driveService.GetPayloadStream(file);

            return new FileStreamResult(payload, "application/octet-stream");
        }

        
        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("thumb")]
        public async Task<IActionResult> GetThumbnail([FromBody] GetThumbnailRequest request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.File.TargetDrive),
                FileId = request.File.FileId
            };

            //TODO: should i write headers indicating the content type for this thumbnail?
            // this.Response.Headers.Add("x-AppData-content-type", new StringValues(""));
            
            var payload = await _driveService.GetThumbnailPayloadStream(file, request.Width, request.Height);
            
            return new FileStreamResult(payload, "application/octet-stream");
        }
        
        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("delete")]
        public async Task DeleteFile([FromBody] ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };
            await _driveService.DeleteLongTermFile(file);
        }
    }

    public class SaveThumbnailRequest
    {
        public ExternalFileIdentifier File { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}