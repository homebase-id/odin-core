using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Authentication.App;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Apps.Drive
{
    [ApiController]
    [Route("/api/owner/v1/drive/query")]
    [Authorize(Policy = AppPolicies.IsAuthorizedApp, AuthenticationSchemes = AppAuthConstants.SchemeName)]
    public class DriveQueryController : ControllerBase
    {
        private readonly IDriveQueryService _driveQueryService;

        public DriveQueryController(IDriveQueryService driveQueryService)
        {
            _driveQueryService = driveQueryService;
        }

        [HttpGet("category")]
        public async Task<IActionResult> GetItemsByCategory(Guid driveId, Guid categoryId, bool includeContent, int pageNumber, int pageSize)
        {
            var page = await _driveQueryService.GetItemsByCategory(driveId, categoryId, includeContent, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }
        
        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentlyCreatedItems(Guid driveId,  bool includeContent, int pageNumber, int pageSize)
        {
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