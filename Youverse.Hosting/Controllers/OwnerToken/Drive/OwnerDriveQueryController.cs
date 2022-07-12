using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.Sqlite.Storage;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveQueryV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveQueryController : ControllerBase
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveQueryService _driveQueryService;
        private readonly IDriveService _driveService;

        public OwnerDriveQueryController(IDriveQueryService driveQueryService, DotYouContextAccessor contextAccessor, IDriveService driveService)
        {
            _driveQueryService = driveQueryService;
            _contextAccessor = contextAccessor;
            _driveService = driveService;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("recent")]
        public async Task<IActionResult> GetRecent([FromBody] QueryParams qp, [FromQuery] GetRecentQueryResultOptions options)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(drive);
            var batch = await _driveQueryService.GetRecent(qp.TargetDrive, options.MaxDate, options.Cursor, qp, options.ToResultOptions());
            return new JsonResult(batch);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("batch")]
        public async Task<IActionResult> GetBatch([FromBody] QueryParams qp, [FromQuery] GetBatchQueryResultOptions options)
        {
            var bytes = Convert.FromBase64String();
            var (p1, p2, p3) = ByteArrayUtil.Split(bytes, 16, 16, 16);

            var cursor = new QueryBatchCursor()
            {
                pagingCursor = p1,
                currentBoundaryCursor = p2,
                nextBoundaryCursor = p3
            };
            
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(qp.TargetDrive);
            var batch = await _driveQueryService.GetBatch(driveId, qp, options.ToResultOptions());
            return new JsonResult(batch);
        }
    }
}