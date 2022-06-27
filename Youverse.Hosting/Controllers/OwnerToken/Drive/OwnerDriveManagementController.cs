using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveManagementV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveManagementController : ControllerBase
    {
        private readonly IDriveQueryService _queryService;
        private readonly IDriveService _driveService;

        public OwnerDriveManagementController(IDriveQueryService queryService, IDriveService driveService)
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