using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveManagementV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveManagementController : ControllerBase
    {
        private readonly IDriveService _driveService;

        public OwnerDriveManagementController( IDriveService driveService)
        {
            _driveService = driveService;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost]
        public async Task<PagedResult<OwnerClientDriveData>> GetDrives([FromBody]GetDrivesRequest request)
        {
            var drives = await _driveService.GetDrives(new PageOptions(request.PageNumber, request.PageSize));

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
            return page;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("create")]
        public async Task<bool> CreateDrive([FromBody] CreateDriveRequest request)
        {
            //create a drive on the drive service
            var _ = await _driveService.CreateDrive(request.Name, request.TargetDrive, request.Metadata, request.AllowAnonymousReads);
            return true;
        }
        
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("type")]
        public async Task<PagedResult<OwnerClientDriveData>> GetDrivesByType(GetDrivesByTypeRequest request)
        {
            var drives = await _driveService.GetDrives(request.DriveType, new PageOptions(request.PageNumber, request.PageSize));
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
            return page;
        }
    }
}