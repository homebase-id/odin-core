using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [Route(YouAuthApiPathConstants.DrivesV1)]
    [AuthorizeValidExchangeGrant]
    public class DriveMetadataController : ControllerBase
    {
        private readonly IDriveService _driveService;

        public DriveMetadataController(IDriveService driveService)
        {
            _driveService = driveService;
        }

        [HttpGet("metadata/type")]
        public async Task<IActionResult> GetDrivesByType(Guid type, int pageNumber, int pageSize)
        {
            var drives = await _driveService.GetDrives(type, new PageOptions(pageNumber, pageSize));

            var clientDriveData = drives.Results.Select(drive =>
                new ClientDriveData()
                {
                    Name = drive.Name,
                    Type = drive.Type,
                    Alias = drive.Alias,
                    Metadata = drive.Metadata //TODO should we return metadata to youauth?
                }).ToList();

            var page = new PagedResult<ClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return new JsonResult(page);
        }
    }
}