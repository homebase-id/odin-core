using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.ClientToken;

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
        [HttpGet("header")]
        public async Task<IActionResult> GetMetadata([FromQuery] TargetDrive drive, [FromQuery] Guid fileId)
        {
            
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive),
                FileId = fileId
            };
            var result = await _appService.GetClientEncryptedFileHeader(file);
            return new JsonResult(result);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpGet("payload")]
        public async Task<IActionResult> StreamPayload([FromQuery] TargetDrive drive, [FromQuery] Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive),
                FileId = fileId
            };
    
            var payload = await _driveService.GetPayloadStream(file);

            return new FileStreamResult(payload, "application/octet-stream");
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpDelete]
        public async Task DeleteFile([FromQuery] TargetDrive drive, [FromQuery] Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive),
                FileId = fileId
            };
            await _driveService.DeleteLongTermFile(file);
        }
    }
}