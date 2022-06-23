using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Authentication.YouAuth;
using Youverse.Hosting.Controllers.Apps;
using Youverse.Hosting.Controllers.Owner;
using Youverse.Hosting.Controllers.YouAuth;

namespace Youverse.Hosting.Controllers.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [Route(OwnerApiPathConstants.DrivesV1)]
    [Route(YouAuthApiPathConstants.DrivesV1)]
    [AuthorizeOwnerConsoleOrApp]
    [Authorize(AuthenticationSchemes = YouAuthConstants.Scheme)]
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