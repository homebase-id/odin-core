using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Owner;

namespace Youverse.Hosting.Controllers.Apps.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1 + "/query")]
    [Route(OwnerApiPathConstants.DrivesV1 + "/query")]
    [AuthorizeOwnerConsoleOrApp]
    public class DriveQueryController : ControllerBase
    {
        private readonly DotYouContext _context;
        private readonly IDriveQueryService _driveQueryService;

        public DriveQueryController(IDriveQueryService driveQueryService, DotYouContext context)
        {
            _driveQueryService = driveQueryService;
            _context = context;
        }

        [HttpGet("tag")]
        public async Task<IActionResult> GetByTag(Guid tag, bool includeContent, int pageNumber, int pageSize)
        {
            var driveId = _context.AppContext.DriveId.GetValueOrDefault();
            var page = await _driveQueryService.GetByTag(driveId, tag, includeContent, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentlyCreatedItems(bool includeContent, int pageNumber, int pageSize)
        {
            var driveId = _context.AppContext.DriveId.GetValueOrDefault();
            var page = await _driveQueryService.GetRecentlyCreatedItems(driveId, includeContent, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpPost("rebuild")]
        public async Task<bool> Rebuild(Guid driveId)
        {
            await _driveQueryService.RebuildBackupIndex(driveId);
            return true;
        }
    }
}