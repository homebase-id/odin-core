using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
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
        public async Task<IActionResult> GetMetadata(Guid driveIdentifier, Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().AppContext.GetDriveId(driveIdentifier),
                FileId = fileId
            };
            var result = await _appService.GetClientEncryptedFileHeader(file);
            return new JsonResult(result);
        }

        [HttpGet("files/payload")]
        public async Task<IActionResult> GetPayload(Guid driveIdentifier, Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().AppContext.GetDriveId(driveIdentifier),
                FileId = fileId
            };

            var payload = await _driveService.GetPayloadStream(file);

            return new FileStreamResult(payload, "application/octet-stream");
        }
    }
}