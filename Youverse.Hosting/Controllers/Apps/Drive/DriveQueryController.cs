using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Hosting.Controllers.Owner;

namespace Youverse.Hosting.Controllers.Apps.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1 + "/query")]
    [Route(OwnerApiPathConstants.DrivesV1 + "/query")]
    [AuthorizeOwnerConsoleOrApp]
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

        [HttpGet("filetype")]
        public async Task<IActionResult> GetByFileType(Guid driveAlias, int fileType, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(driveAlias);
            ;
            var page = await _driveQueryService.GetByFileType(driveId, fileType, includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpGet("alias")]
        public async Task<IActionResult> GetByAlias(Guid driveAlias, Guid alias, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(driveAlias);
            ;
            var page = await _driveQueryService.GetByAlias(driveId, alias, includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpGet("tag")]
        public async Task<IActionResult> GetByTag(Guid driveAlias, Guid tag, int fileType, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(driveAlias);
            ;

            var page = await _driveQueryService.GetByTag(driveId, tag, fileType, includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentlyCreatedItems(Guid driveAlias, bool includeMetadataHeader, bool includePayload, int pageNumber, int pageSize)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(driveAlias);
            ;
            var page = await _driveQueryService.GetRecentlyCreatedItems(driveId, includeMetadataHeader, includePayload, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpPost("recent2")]
        public async Task<IActionResult> GetRecent(Guid driveAlias, UInt64 maxDate, byte[] startCursor, [FromBody] QueryParams qp, [FromBody] ResultOptions options)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(driveAlias);
            var batch = await _driveQueryService.GetRecent(driveId, maxDate, startCursor, qp, options);
            return new JsonResult(batch);
        }

        [HttpPost("batch")]
        public async Task<IActionResult> GetBatch(Guid driveAlias, byte[] startCursor, byte[] stopCursor, [FromBody] QueryParams qp, [FromBody] ResultOptions options)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(driveAlias);
            var batch = await _driveQueryService.GetBatch(driveId, startCursor, stopCursor, qp, options);
            return new JsonResult(batch);
        }

        [HttpPost("rebuild")]
        public async Task<bool> Rebuild(Guid driveId)
        {
            await _driveQueryService.RebuildBackupIndex(driveId);
            return true;
        }
    }
}