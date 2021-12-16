using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Drive.Query;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.Drive
{
    [ApiController]
    [Route("/api/owner/v1/drive/query")]
    [Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
    public class DriveQueryController : ControllerBase
    {
        private readonly IDriveQueryService _queryService;

        public DriveQueryController(IDriveQueryService queryService)
        {
            _queryService = queryService;
        }

        [HttpGet("category")]
        public async Task<IActionResult> GetItemsByCategory(Guid driveId, Guid categoryId, bool includeContent, int pageNumber, int pageSize)
        {
            var page = await _queryService.GetItemsByCategory(driveId, categoryId, includeContent, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }
        
        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentlyCreatedItems(Guid driveId,  bool includeContent, int pageNumber, int pageSize)
        {
            var page = await _queryService.GetRecentlyCreatedItems(driveId, includeContent, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }
    }
}