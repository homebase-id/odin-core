using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.Drive
{
    [ApiController]
    // [Route(OwnerApiPathConstants.DrivesV1)]
    [Route("/api/owner/v1/drive/index")]
    [AuthorizeOwnerConsole]
    public class DriveManagementController : ControllerBase
    {
        private readonly IDriveQueryService _queryService;
        private readonly IDriveService _driveService;

        public DriveManagementController(IDriveQueryService queryService, IDriveService driveService)
        {
            _queryService = queryService;
            _driveService = driveService;
        }

        [HttpPost("rebuildallindices")]
        public async Task<bool> RebuildAll()
        {
            await _queryService.RebuildAllIndices();
            return true;
        }
        
        [HttpPost("rebuildindex")]
        public async Task<bool> Rebuild(Guid driveId)
        {
            await _queryService.RebuildBackupIndex(driveId);
            return true;
        }
    }
}