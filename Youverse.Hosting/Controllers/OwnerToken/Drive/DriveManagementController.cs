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
    [Route(OwnerApiPathConstants.DrivesV1)]
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

        [HttpGet]
        public async Task<IActionResult> GetDrives(int pageNumber, int pageSize)
        {
            var drives = await _driveService.GetDrives(new PageOptions(pageNumber, pageSize));

            var clientDriveData = drives.Results.Select(drive =>
                new OwnerClientDriveData()
                {
                    Name = drive.Name,
                    Type = drive.Type,
                    Alias = drive.Alias,
                    Metadata = drive.Metadata,
                    IsReadonly = drive.IsReadonly,
                    AllowAnonymousReads = drive.AllowAnonymousReads
                }).ToList();

            var page = new PagedResult<OwnerClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return new JsonResult(page);
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateDrive(TargetDrive targetDrive, string name, string metadata, bool allowAnonymousReads)
        {
            //create a drive on the drive service
            var _ = await _driveService.CreateDrive(name, targetDrive, metadata, allowAnonymousReads);
            return Ok();
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