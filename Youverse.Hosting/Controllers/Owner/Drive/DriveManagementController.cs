using System;
using System.Linq;
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
    [Route("/api/owner/v1/drive/mgmt")]
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

        [HttpGet("drives")]
        public async Task<IActionResult> GetDrives(int pageNumber, int pageSize)
        {
            var drives = await _driveService.GetDrives(new PageOptions(pageNumber, pageSize));

            var clientDriveData = drives.Results.Select(drive =>
                new ClientDriveData()
                {
                    Name = drive.Name,
                    Type = drive.Type,
                    Alias = drive.Alias,
                    Metadata = drive.Metadata,
                    IsReadonly = drive.IsReadonly,
                    AllowAnonymousReads = drive.AllowAnonymousReads
                }).ToList();

            var page = new PagedResult<ClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return new JsonResult(page);
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