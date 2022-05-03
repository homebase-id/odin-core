using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Owner;
using Youverse.Hosting.Controllers.YouAuth;


namespace Youverse.Hosting.Controllers.Apps.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [Route(OwnerApiPathConstants.DrivesV1)]
    [AuthorizeOwnerConsoleOrApp]
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
        public async Task<IActionResult> GetMetadata(Guid driveAlias, Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _driveService.GetDriveIdByAlias(driveAlias).Result.GetValueOrDefault(),
                FileId = fileId
            };
            var result = await _appService.GetClientEncryptedFileHeader(file);
            return new JsonResult(result);
        }

        [HttpGet("files/payload")]
        public async Task<IActionResult> GetPayload(Guid driveAlias, Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _driveService.GetDriveIdByAlias(driveAlias).Result.GetValueOrDefault(),
                FileId = fileId
            };

            var payload = await _driveService.GetPayloadStream(file);

            return new FileStreamResult(payload, "application/octet-stream");
        }

        [HttpDelete("files")]
        public async Task DeleteFile(Guid driveAlias, Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _driveService.GetDriveIdByAlias(driveAlias).Result.GetValueOrDefault(),
                FileId = fileId
            };
            await _driveService.DeleteLongTermFile(file);
        }
    }
}