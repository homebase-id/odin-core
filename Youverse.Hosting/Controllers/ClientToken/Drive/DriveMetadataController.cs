using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.OwnerToken;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

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

        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("metadata/type")]
        public async Task<PagedResult<ClientDriveData>> GetDrivesByType([FromQuery] GetDrivesByTypeRequest request)
        {
            var drives = await _driveService.GetDrives(request.DriveType, new PageOptions(request.PageNumber, request.PageSize));

            var clientDriveData = drives.Results.Select(drive =>
                new ClientDriveData()
                {
                    Name = drive.Name,
                    Type = drive.Type,
                    Alias = drive.Alias,
                    Metadata = drive.Metadata //TODO should we return metadata to youauth?
                }).ToList();

            var page = new PagedResult<ClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return page;
        }
    }
}
