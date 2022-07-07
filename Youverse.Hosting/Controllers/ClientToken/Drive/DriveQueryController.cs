using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1 + "/query")]
    [Route(YouAuthApiPathConstants.DrivesV1 + "/query")]
    [AuthorizeValidExchangeGrant]
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

        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("recent")]
        public async Task<IActionResult> GetRecent([FromQuery] TargetDrive drive, [FromQuery] UInt64 maxDate, [FromQuery] byte[] startCursor, [FromBody] QueryParams qp,
            [FromQuery] ResultOptions options)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive);
            var batch = await _driveQueryService.GetRecent(driveId, maxDate, startCursor, qp, options);
            return new JsonResult(batch);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("batch")]
        public async Task<IActionResult> GetBatch([FromBody] QueryParams qp, [FromQuery] ResultOptions options)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(qp.Drive);
            var batch = await _driveQueryService.GetBatch(driveId, qp, options);
            return new JsonResult(batch);
        }
    }
}