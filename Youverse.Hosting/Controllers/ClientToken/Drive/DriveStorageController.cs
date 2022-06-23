using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Authentication.YouAuth;
using Youverse.Hosting.Controllers.Apps;
using Youverse.Hosting.Controllers.Owner;
using Youverse.Hosting.Controllers.YouAuth;

namespace Youverse.Hosting.Controllers.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [Route(OwnerApiPathConstants.DrivesV1)]
    [Route(YouAuthApiPathConstants.DrivesV1)]
    [AuthorizeOwnerConsoleOrApp]
    [Authorize(AuthenticationSchemes = YouAuthConstants.Scheme)]
    public class DriveStorageController : ControllerBase
    {
        private readonly IAppService _appService;
        private readonly IDriveService _driveService;
        private readonly DotYouContextAccessor _contextAccessor;

        public DriveStorageController(DotYouContextAccessor contextAccessor, IDriveService driveService, IAppService appService)
        {
            _contextAccessor = contextAccessor;
            _driveService = driveService;
            _appService = appService;
        }

        [HttpGet("files/header")]
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

        [HttpGet("files/payload")]
        public async Task<IActionResult> GetPayload([FromQuery] TargetDrive drive, [FromQuery] Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive),
                FileId = fileId
            };
    
            var payload = await _driveService.GetPayloadStream(file);

            return new FileStreamResult(payload, "application/octet-stream");
        }

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
    }
}