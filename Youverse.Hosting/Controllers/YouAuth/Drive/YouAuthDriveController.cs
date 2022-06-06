using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Hosting.Authentication.YouAuth;

namespace Youverse.Hosting.Controllers.YouAuth.Drive
{
    [ApiController]
    [Route(YouAuthApiPathConstants.DrivesV1)]
    [Authorize(AuthenticationSchemes = YouAuthConstants.Scheme)]
    public class YouAuthDriveStorageController : ControllerBase
    {
        private readonly IAppService _appService;
        private readonly IDriveService _driveService;
        private readonly IDriveQueryService _driveQueryService;

        private readonly DotYouContextAccessor _contextAccessor;

        public YouAuthDriveStorageController(DotYouContextAccessor contextAccessor, IDriveService driveService, IAppService appService, IDriveQueryService driveQueryService)
        {
            _contextAccessor = contextAccessor;
            _driveService = driveService;
            _appService = appService;
            _driveQueryService = driveQueryService;
        }

        //

        [HttpGet("files/header")]
        public async Task<IActionResult> GetMetadata([FromQuery] TargetDrive drive, [FromQuery] Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive),
                FileId = fileId
            };

            //TODO: this call will encrypt the file header using the app shared secret, yet in youauth - we are using the exchange token
            //also, there may not be an exchange token in the case of a call to an anonymous file

            var result = await _appService.GetClientEncryptedFileHeader(file);
            return new JsonResult(result);
        }

        [HttpGet("files/payload")]
        public async Task<IActionResult> GetPayload([FromQuery] TargetDrive drive,[FromQuery]  Guid fileId)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive),
                FileId = fileId
            };

            var payload = await _driveService.GetPayloadStream(file);
            return new FileStreamResult(payload, "application/octet-stream");
        }
        
        [HttpPost("query/recent")]
        public async Task<IActionResult> GetRecent([FromQuery] TargetDrive drive, [FromQuery] UInt64 maxDate, [FromQuery] byte[] startCursor, [FromBody] QueryParams qp,
            [FromQuery] ResultOptions options)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive);
            var batch = await _driveQueryService.GetRecent(driveId, maxDate, startCursor, qp, options);
            return new JsonResult(batch);
        }

        [HttpPost("query/batch")]
        public async Task<IActionResult> GetBatch([FromQuery]TargetDrive drive, [FromQuery] byte[] startCursor, [FromQuery] byte[] stopCursor, [FromBody] QueryParams qp,
            [FromQuery] ResultOptions options)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive);
            var batch = await _driveQueryService.GetBatch(driveId, startCursor, stopCursor, qp, options);
            return new JsonResult(batch);
        }

        [HttpGet("metadata/type")]
        public async Task<IActionResult> GetDrivesByType(Guid type, int pageNumber, int pageSize)
        {
            var drives = await _driveService.GetDrives(type, new PageOptions(pageNumber, pageSize));

            var clientDriveData = drives.Results.Select(drive =>
                new YouAuthClientDriveData()
                {
                    Name = drive.Name,
                    Type = drive.Type,
                    Alias = drive.Alias
                }).ToList();

            var page = new PagedResult<YouAuthClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return new JsonResult(page);
        }
    }
}