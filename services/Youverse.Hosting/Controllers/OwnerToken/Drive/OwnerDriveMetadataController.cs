using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.ClientToken.Drive;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DrivesV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveMetadataController : ControllerBase
    {
        private readonly DriveManager _driveManager;

        public OwnerDriveMetadataController(DriveManager driveManager)
        {
            _driveManager = driveManager;
        }

        /// <summary>
        /// Gets a list of drives by their type
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("metadata/type")]
        public async Task<PagedResult<OwnerClientDriveData>> GetDrivesByType([FromQuery] GetDrivesByTypeRequest request)
        {
            var drives = await _driveManager.GetDrives(request.DriveType, new PageOptions(request.PageNumber, request.PageSize));

            var clientDriveData = drives.Results.Select(drive =>
                new OwnerClientDriveData()
                {
                    Name = drive.Name,
                    TargetDriveInfo = drive.TargetDriveInfo,
                    Metadata = drive.Metadata,
                    IsReadonly = drive.IsReadonly,
                    AllowAnonymousReads = drive.AllowAnonymousReads,
                    OwnerOnly = drive.OwnerOnly
                }).ToList();

            var page = new PagedResult<OwnerClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return page;
        }
    }
}