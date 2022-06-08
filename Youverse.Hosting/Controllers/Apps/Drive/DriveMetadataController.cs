using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Owner;


namespace Youverse.Hosting.Controllers.Apps.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    // [Route(OwnerApiPathConstants.DrivesV1)]
    [AuthorizeOwnerConsoleOrApp]
    public class DriveMetadataController : ControllerBase
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveService _driveService;
        private readonly ExchangeGrantService _exchangeGrantService;

        public DriveMetadataController(DotYouContextAccessor contextAccessor, IDriveService driveService, ExchangeGrantService exchangeGrantService)
        {
            _contextAccessor = contextAccessor;
            _driveService = driveService;
            _exchangeGrantService = exchangeGrantService;
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
                    Metadata = drive.Metadata
                }).ToList();

            var page = new PagedResult<ClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return new JsonResult(page);
        }

    }
}