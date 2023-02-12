using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveManagementV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveManagementController : ControllerBase
    {
        private readonly IDriveService _driveService;
        private readonly DriveManager _driveManager;

        public OwnerDriveManagementController(IDriveService driveService, DriveManager driveManager)
        {
            _driveService = driveService;
            _driveManager = driveManager;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost]
        public async Task<PagedResult<OwnerClientDriveData>> GetDrives([FromBody] GetDrivesRequest request)
        {
            var drives = await _driveManager.GetDrives(new PageOptions(request.PageNumber, request.PageSize));

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

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("create")]
        public async Task<bool> CreateDrive([FromBody] CreateDriveRequest request)
        {
            //create a drive on the drive service
            var _ = await _driveManager.CreateDrive(request);
            return true;
        }

        [HttpPost("setdrivemode")]
        public async Task<bool> SetDriveReadMode([FromBody] UpdateDriveDefinitionRequest request)
        {
            var driveId = await _driveManager.GetDriveIdByAlias(request.TargetDrive, true);
            await _driveManager.SetDriveReadMode(driveId.GetValueOrDefault(), request.AllowAnonymousReads);
            return true;
        }
        
        [HttpPost("updatemetadata")]
        public async Task<bool> UpdateDriveMetadata([FromBody] UpdateDriveDefinitionRequest request)
        {
            var driveId = await _driveManager.GetDriveIdByAlias(request.TargetDrive, true);
            await _driveManager.UpdateMetadata(driveId.GetValueOrDefault(), request.Metadata);
            return true;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("type")]
        public async Task<PagedResult<OwnerClientDriveData>> GetDrivesByType([FromBody]GetDrivesByTypeRequest request)
        {
            var drives = await _driveManager.GetDrives(request.DriveType, new PageOptions(request.PageNumber, request.PageSize));
            var clientDriveData = drives.Results.Select(drive =>
                new OwnerClientDriveData()
                {
                    Name = drive.Name,
                    TargetDriveInfo = drive.TargetDriveInfo,
                    Metadata = drive.Metadata,
                    IsReadonly = drive.IsReadonly,
                    AllowAnonymousReads = drive.AllowAnonymousReads
                }).ToList();

            var page = new PagedResult<OwnerClientDriveData>(drives.Request, drives.TotalPages, clientDriveData);
            return page;
        }
    }

    public class UpdateDriveDefinitionRequest
    {
        public TargetDrive TargetDrive { get; set; }
        public bool AllowAnonymousReads { get; set; }
        
        public string Metadata { get; set; }
    }
}