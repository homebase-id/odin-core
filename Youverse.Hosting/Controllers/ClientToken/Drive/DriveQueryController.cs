using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Hosting.Authentication.YouAuth;
using Youverse.Hosting.Controllers.Apps;
using Youverse.Hosting.Controllers.Owner;
using Youverse.Hosting.Controllers.YouAuth;

namespace Youverse.Hosting.Controllers.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1 + "/query")]
    [Route(OwnerApiPathConstants.DrivesV1 + "/query")]
    [Route(YouAuthApiPathConstants.DrivesV1 + "/query")]
    //[AuthorizeOwnerConsoleOrApp]
    // [Authorize(AuthenticationSchemes = YouAuthConstants.Scheme)]
    [AuthorizeOwnerAppYouAuth]
    public class DriveQueryController : ControllerBase
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveQueryService _driveQueryService;
        private readonly IDriveService _driveService;

        public DriveQueryController(IDriveQueryService driveQueryService, DotYouContextAccessor contextAccessor, IDriveService driveService)
        {
            _driveQueryService = driveQueryService;
            _contextAccessor = contextAccessor;
            _driveService = driveService;
        }
        
        [HttpPost("recent")]
        public async Task<IActionResult> GetRecent([FromQuery] TargetDrive drive, [FromQuery] UInt64 maxDate, [FromQuery] byte[] startCursor, [FromBody] QueryParams qp,
            [FromQuery] ResultOptions options)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive);
            var batch = await _driveQueryService.GetRecent(driveId, maxDate, startCursor, qp, options);
            return new JsonResult(batch);
        }

        [HttpPost("batch")]
        public async Task<IActionResult> GetBatch([FromQuery] TargetDrive drive, [FromQuery] byte[] startCursor, [FromQuery] byte[] stopCursor, [FromBody] QueryParams qp,
            [FromQuery] ResultOptions options)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive);
            var batch = await _driveQueryService.GetBatch(driveId, startCursor, stopCursor, qp, options);
            return new JsonResult(batch);
        }
    }
}